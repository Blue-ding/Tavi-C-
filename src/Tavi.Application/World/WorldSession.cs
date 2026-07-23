using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>
/// 持有当前运行时世界，并统一控制查询、原子修改、撤销、变化通知、加载和自动保存。真实 World 不向外暴露。
/// </summary>
public sealed class WorldSession : IAsyncDisposable
{
    private readonly IWorldStore _store;
    private readonly string _slot;
    private readonly TimeSpan _autoSaveDelay;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _worldSync = new();
    private readonly object _debounceSync = new();
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly Stack<AppliedWorldChangeSet> _undoHistory = new();
    private readonly Stack<AppliedWorldChangeSet> _redoHistory = new();
    private CancellationTokenSource? _debounceSource;
    private RuntimeWorld? _current;
    private long _savedRevision;
    private int _reportedDirty;
    private bool _disposed;

    /// <summary>
    /// 创建世界会话。
    /// </summary>
    public WorldSession(IWorldStore store, string slot = "default", TimeSpan? autoSaveDelay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("存档槽名称不能为空。", nameof(slot));
        _slot = slot;
        _autoSaveDelay = autoSaveDelay ?? TimeSpan.FromSeconds(1);
        if (_autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay), "自动保存延迟不能为负数。");
        Queries = new WorldQueries(this);
    }

    /// <summary>
    /// 在一个原子操作组成功提交后触发；事件处理器在写锁释放后执行。
    /// </summary>
    public event EventHandler<WorldSessionChangedEventArgs>? Changed;

    /// <summary>
    /// 在脏状态或保存状态发生变化后触发。
    /// </summary>
    public event EventHandler<WorldSessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 获取始终通过本会话同步边界读取最新状态的查询工具；查询器自身不缓存 World 数据。
    /// </summary>
    public WorldQueries Queries { get; }

    /// <summary>
    /// 获取当前会话健康状态。回滚失败后会话进入 Faulted，并拒绝继续读写或保存。
    /// </summary>
    public WorldSessionHealth Health { get; private set; } = WorldSessionHealth.Healthy;

    /// <summary>
    /// 获取当前 World revision。
    /// </summary>
    public long Revision => ExecuteQuery(world => world.Revision);

    /// <summary>
    /// 获取会话是否包含尚未保存的修改。
    /// </summary>
    public bool IsDirty => ExecuteQuery(world => world.Revision != Volatile.Read(ref _savedRevision));

    /// <summary>
    /// 获取当前是否存在可撤销的已提交操作组。
    /// </summary>
    public bool CanUndo => ExecuteLocked(() => _undoHistory.Count > 0);

    /// <summary>
    /// 获取当前是否存在可重做的操作组。
    /// </summary>
    public bool CanRedo => ExecuteLocked(() => _redoHistory.Count > 0);

    /// <summary>
    /// 获取最近一次后台自动保存异常。
    /// </summary>
    public Exception? LastAutoSaveException { get; private set; }

    /// <summary>
    /// 从存档槽加载世界；存档不存在时创建新世界。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_worldSync)
        {
            if (_current is not null)
                throw new InvalidOperationException("WorldSession 已经初始化。");
        }
        WorldSnapshot? snapshot = await _store.LoadAsync(_slot, cancellationToken);
        lock (_worldSync)
        {
            if (_current is not null)
                throw new InvalidOperationException("WorldSession 已经初始化。");
            _current = RuntimeWorld.Create(snapshot ?? new WorldSnapshot());
            _undoHistory.Clear();
            _redoHistory.Clear();
            Health = WorldSessionHealth.Healthy;
            Volatile.Write(ref _savedRevision, snapshot is null ? -1 : _current.Revision);
        }
        NotifyDirtyChanged();
        if (snapshot is null)
            ScheduleAutoSave();
    }

    /// <summary>
    /// 以 expectedRevision 为乐观并发条件原子提交操作组。中间操作不产生事件，失败且回滚成功时 World 和 revision 保持不变。
    /// </summary>
    public WorldCommitResult Apply(WorldChangeSet changeSet, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        WorldCommitResult result = ApplyCore(changeSet, expectedRevision, HistoryAction.Record);
        PublishCommit(result, WorldSessionOperation.Apply);
        return result;
    }

    /// <summary>
    /// 原子应用最近一次提交的反向操作。撤销本身是新提交，因此 revision 继续递增。
    /// </summary>
    public WorldCommitResult Undo(long expectedRevision)
    {
        WorldCommitResult result = ApplyCore(null, expectedRevision, HistoryAction.Undo);
        PublishCommit(result, WorldSessionOperation.Undo);
        return result;
    }

    /// <summary>
    /// 原子重新应用最近一次撤销的正向操作。重做本身是新提交，因此 revision 继续递增。
    /// </summary>
    public WorldCommitResult Redo(long expectedRevision)
    {
        WorldCommitResult result = ApplyCore(null, expectedRevision, HistoryAction.Redo);
        PublishCommit(result, WorldSessionOperation.Redo);
        return result;
    }

    /// <summary>
    /// 立即保存当前世界，无论当前是否为脏状态。
    /// </summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        CancelPendingAutoSave();
        return SaveCoreAsync(true, cancellationToken);
    }

    /// <summary>
    /// 如果当前世界包含未保存修改，则立即写入存档。
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        CancelPendingAutoSave();
        return SaveCoreAsync(false, cancellationToken);
    }

    /// <summary>
    /// 取消自动保存并在释放健康会话前刷新未保存修改；Faulted 会话不会覆盖可靠存档。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        CancelPendingAutoSave();
        try
        {
            if (_current is not null && Health == WorldSessionHealth.Healthy)
                await SaveCoreAsync(false, CancellationToken.None);
        }
        finally
        {
            _disposed = true;
            _lifetimeSource.Cancel();
            _lifetimeSource.Dispose();
            _saveGate.Dispose();
        }
    }

    /// <summary>
    /// 在一次锁持有期间完成整个查询和结果物化。query 仅供同程序集的查询器使用，不能把 RuntimeWorld 或内部实体引用返回给调用方。
    /// </summary>
    internal TResult ExecuteQuery<TResult>(Func<RuntimeWorld, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ExecuteLocked(() => query(RequireCurrent()));
    }

    private WorldCommitResult ApplyCore(WorldChangeSet? requestedChangeSet, long expectedRevision, HistoryAction historyAction)
    {
        lock (_worldSync)
        {
            EnsureUsableLocked();
            RuntimeWorld world = RequireCurrent();
            if (world.Revision != expectedRevision)
                throw new WorldRevisionConflictException(expectedRevision, world.Revision);
            AppliedWorldChangeSet? historyEntry = historyAction switch
            {
                HistoryAction.Undo => _undoHistory.TryPeek(out AppliedWorldChangeSet? undo) ? undo : throw new InvalidOperationException("没有可撤销的世界操作。"),
                HistoryAction.Redo => _redoHistory.TryPeek(out AppliedWorldChangeSet? redo) ? redo : throw new InvalidOperationException("没有可重做的世界操作。"),
                _ => null
            };
            WorldChangeSet changeSet = historyAction switch
            {
                HistoryAction.Undo => historyEntry!.Inverse,
                HistoryAction.Redo => historyEntry!.Forward,
                _ => requestedChangeSet!
            };
            WorldApplyResult applied;
            try
            {
                applied = world.Apply(changeSet);
            }
            catch (WorldTransactionException)
            {
                Health = WorldSessionHealth.Faulted;
                CancelPendingAutoSave();
                throw;
            }
            if (!applied.Changed)
                return WorldCommitResult.Unchanged(world.Revision);
            switch (historyAction)
            {
                case HistoryAction.Record:
                    _undoHistory.Push(applied.ChangeSet!);
                    _redoHistory.Clear();
                    break;
                case HistoryAction.Undo:
                    _undoHistory.Pop();
                    _redoHistory.Push(historyEntry!);
                    break;
                case HistoryAction.Redo:
                    _redoHistory.Pop();
                    _undoHistory.Push(historyEntry!);
                    break;
            }
            return new WorldCommitResult(Guid.NewGuid(), applied.PreviousRevision, applied.Revision, applied.ChangeSet);
        }
    }

    private void PublishCommit(WorldCommitResult result, WorldSessionOperation operation)
    {
        if (!result.Changed)
            return;
        ScheduleAutoSave();
        Changed?.Invoke(this, new WorldSessionChangedEventArgs(result.CommitId, result.Revision, operation, result.ChangeSet!));
        NotifyDirtyChanged();
    }

    private async Task SaveCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            (WorldSnapshot Snapshot, long Revision) state = ExecuteQuery(world => (world.CreateSnapshot(), world.Revision));
            if (!force && state.Revision == Volatile.Read(ref _savedRevision))
                return;
            RaiseStateChanged(WorldSessionStateChange.SaveStarted);
            try
            {
                await _store.SaveAsync(_slot, state.Snapshot, cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                RaiseStateChanged(WorldSessionStateChange.SaveCancelled, exception);
                throw;
            }
            catch (Exception exception)
            {
                RaiseStateChanged(WorldSessionStateChange.SaveFailed, exception);
                throw;
            }
            Volatile.Write(ref _savedRevision, state.Revision);
            LastAutoSaveException = null;
            NotifyDirtyChanged();
            RaiseStateChanged(WorldSessionStateChange.SaveCompleted);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ScheduleAutoSave()
    {
        if (Health != WorldSessionHealth.Healthy)
            return;
        CancellationTokenSource source;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource?.Dispose();
            source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeSource.Token);
            _debounceSource = source;
        }
        _ = RunAutoSaveAsync(source);
    }

    private async Task RunAutoSaveAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(_autoSaveDelay, source.Token);
            await SaveCoreAsync(false, source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LastAutoSaveException = exception;
        }
        finally
        {
            lock (_debounceSync)
            {
                if (ReferenceEquals(_debounceSource, source))
                    _debounceSource = null;
            }
            source.Dispose();
        }
    }

    private void CancelPendingAutoSave()
    {
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource = null;
        }
    }

    private void NotifyDirtyChanged()
    {
        bool isDirty = IsDirty;
        int value = isDirty ? 1 : 0;
        if (Interlocked.Exchange(ref _reportedDirty, value) != value)
            StateChanged?.Invoke(this, new WorldSessionStateChangedEventArgs(WorldSessionStateChange.DirtyChanged, isDirty));
    }

    private void RaiseStateChanged(WorldSessionStateChange change, Exception? exception = null)
    {
        StateChanged?.Invoke(this, new WorldSessionStateChangedEventArgs(change, IsDirty, exception));
    }

    private TResult ExecuteLocked<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_worldSync)
        {
            EnsureUsableLocked();
            return operation();
        }
    }

    private RuntimeWorld RequireCurrent() => _current ?? throw new InvalidOperationException("WorldSession 尚未初始化。");

    private void EnsureUsable()
    {
        lock (_worldSync)
            EnsureUsableLocked();
    }

    private void EnsureUsableLocked()
    {
        ThrowIfDisposed();
        if (Health == WorldSessionHealth.Faulted)
            throw new InvalidOperationException("WorldSession 因事务回滚失败已进入 Faulted 状态，不能继续读写或保存。");
        _ = RequireCurrent();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private enum HistoryAction
    {
        Record,
        Undo,
        Redo
    }
}
