using Tavi.Domain.World;
using Tavi.Utilities.Concurrency;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>
/// 持有当前运行时世界，并统一控制查询、原子修改、撤销、变化通知、加载和自动保存。真实 World 不向外暴露。
/// </summary>
public sealed class WorldSession : IWorldWorkspace, IWorldSessionLifecycle
{
    private readonly IWorldStore _store;
    private readonly IWorldTypePolicy _typePolicy;
    private readonly string _slot;
    private readonly TimeSpan _autoSaveDelay;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _debounceSync = new();
    private readonly object _disposeSync = new();
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly VersionedWorkspace<RuntimeWorld, WorldChangeSet, AppliedWorldChangeSet> _workspace;
    private readonly WorldStagingArea _staging = new();
    private CancellationTokenSource? _debounceSource;
    private readonly List<Task> _autoSaveTasks = [];
    private Guid _savedStateId;
    private int _reportedDirty;
    private bool _disposed;
    private Task? _disposeTask;

    /// <summary>
    /// 创建世界会话。
    /// </summary>
    public WorldSession(IWorldStore store, IWorldTypePolicy typePolicy, string slot = "default", TimeSpan? autoSaveDelay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _typePolicy = typePolicy ?? throw new ArgumentNullException(nameof(typePolicy));
        if (string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("存档槽名称不能为空。", nameof(slot));
        _slot = slot;
        _autoSaveDelay = autoSaveDelay ?? TimeSpan.FromSeconds(1);
        if (_autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay), "自动保存延迟不能为负数。");
        _workspace = new VersionedWorkspace<RuntimeWorld, WorldChangeSet, AppliedWorldChangeSet>(new WorldConcurrencyModel());
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
    public WorldSessionHealth Health => _workspace.Health == VersionedWorkspaceHealth.Healthy ? WorldSessionHealth.Healthy : WorldSessionHealth.Faulted;

    /// <summary>获取当前 World 状态标识；每次实际提交都会生成新值，该值不表达提交顺序。</summary>
    public Guid StateId => ExecuteQuery(world => world.StateId);

    /// <summary>
    /// 获取会话是否包含尚未保存的修改。
    /// </summary>
    public bool IsDirty => ExecuteQuery(world => world.StateId != _savedStateId);

    /// <summary>
    /// 获取当前是否存在可撤销的已提交操作组。
    /// </summary>
    public bool CanUndo => _workspace.CanUndo;

    /// <summary>
    /// 获取当前是否存在可重做的操作组。
    /// </summary>
    public bool CanRedo => _workspace.CanRedo;

    /// <summary>获取当前暂存区 revision。</summary>
    public long StagingRevision => ExecuteLocked(() => _staging.Revision);

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
        if (_workspace.IsInitialized)
            throw new InvalidOperationException("WorldSession 已经初始化。");
        WorldSnapshot? snapshot = await _store.LoadAsync(_slot, cancellationToken);
        RuntimeWorld world = RuntimeWorld.Create(snapshot ?? new WorldSnapshot());
        try
        {
            _workspace.Initialize(world);
        }
        catch (InvalidOperationException exception) when (_workspace.IsInitialized)
        {
            throw new InvalidOperationException("WorldSession 已经初始化。", exception);
        }
        _workspace.ExecuteExclusive(() =>
        {
            _savedStateId = snapshot is null ? Guid.Empty : world.StateId;
            return true;
        });
        NotifyDirtyChanged();
        if (snapshot is null)
            ScheduleAutoSave();
    }

    /// <summary>
    /// 以 expectedStateId 为乐观并发条件原子提交操作组。中间操作不产生事件，失败且回滚成功时 World 和 StateId 保持不变。
    /// </summary>
    public WorldCommitResult Apply(WorldChangeSet changeSet, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ValidateRegisteredTypes(changeSet.Operations);
        WorldCommitResult result = CommitWorkspace(changeSet, expectedStateId);
        PublishCommit(result, WorldSessionOperation.Apply);
        return result;
    }

    /// <summary>将一项不可变 World 操作追加到暂存日志；暂存不会修改真实 World。</summary>
    public Guid Stage(WorldOperation operation, WorldStagedChangeSource source = WorldStagedChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateRegisteredType(operation);
        return ExecuteLocked(() => _staging.Stage(operation, source));
    }

    /// <summary>将指向同一 World 项目的操作组作为一项原子暂存记录追加。</summary>
    public Guid Stage(WorldChangeSet changeSet, WorldStagedChangeSource source = WorldStagedChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ValidateRegisteredTypes(changeSet.Operations);
        return ExecuteLocked(() => _staging.Stage(changeSet, source));
    }

    /// <summary>将一组不可变 World 操作按顺序追加到暂存日志；全部操作通过结构校验后才会写入。</summary>
    public IReadOnlyList<Guid> Stage(IEnumerable<WorldOperation> operations, WorldStagedChangeSource source)
    {
        ArgumentNullException.ThrowIfNull(operations);
        WorldOperation[] copied = operations.ToArray();
        ValidateRegisteredTypes(copied);
        return ExecuteLocked(() => _staging.Stage(copied, source));
    }

    /// <summary>创建包含全部暂存项及临时 World 投影的不可变快照。</summary>
    public WorldStagingSnapshot CreateStagingSnapshot() => ExecuteLocked(() =>
    {
        RuntimeWorld world = _workspace.Read(value => value);
        return _staging.CreateSnapshot(world.CreateSnapshot(), world.StateId);
    });

    /// <summary>删除指定暂存日志项；不存在时返回 false。</summary>
    public bool DeleteStaged(Guid changeId) => ExecuteLocked(() => _staging.Delete(changeId));

    /// <summary>删除当前全部无效暂存项并返回删除数量；冲突项不会被删除。</summary>
    public int DeleteInvalidStaged() => ExecuteLocked(() => _staging.DeleteInvalid(_workspace.Read(world => world.CreateSnapshot())));

    /// <summary>原子提交选中的有效暂存项并在成功后消费它们。</summary>
    public WorldStagingCommitResult CommitStaged(IEnumerable<Guid> selectedChangeIds, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(selectedChangeIds);
        (WorldCommitResult Commit, Guid[] Selected) transaction = _workspace.ExecuteExclusive(() =>
        {
            RuntimeWorld world = _workspace.Read(value => value);
            (WorldChangeSet changeSet, Guid[] selected) = _staging.PrepareCommit(selectedChangeIds, world.CreateSnapshot());
            WorldCommitResult commit = CommitWorkspace(changeSet, expectedStateId);
            _staging.Consume(selected);
            return (commit, selected);
        });
        PublishCommit(transaction.Commit, WorldSessionOperation.Apply);
        return new WorldStagingCommitResult { Commit = transaction.Commit, ConsumedChangeIds = transaction.Selected };
    }

    /// <summary>
    /// 原子应用最近一次提交的反向操作。撤销本身是新提交，因此会生成新的 StateId。
    /// </summary>
    public WorldCommitResult Undo(Guid expectedStateId)
    {
        WorldCommitResult result = ExecuteWorkspace(() => _workspace.Undo(expectedStateId));
        PublishCommit(result, WorldSessionOperation.Undo);
        return result;
    }

    /// <summary>
    /// 原子重新应用最近一次撤销的正向操作。重做本身是新提交，因此会生成新的 StateId。
    /// </summary>
    public WorldCommitResult Redo(Guid expectedStateId)
    {
        WorldCommitResult result = ExecuteWorkspace(() => _workspace.Redo(expectedStateId));
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
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await CancelAndDrainPendingAutoSaveAsync();
        try
        {
            if (_workspace.IsInitialized && Health == WorldSessionHealth.Healthy)
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
        ThrowIfDisposed();
        return _workspace.Read(query);
    }

    private WorldCommitResult CommitWorkspace(WorldChangeSet changeSet, Guid expectedStateId)
        => ExecuteWorkspace(() => _workspace.Commit(changeSet, expectedStateId));

    private WorldCommitResult ExecuteWorkspace(Func<VersionedCommitResult<AppliedWorldChangeSet>> action)
    {
        try
        {
            VersionedCommitResult<AppliedWorldChangeSet> result = action();
            return result.Changed
                ? new WorldCommitResult(result.CommitId, result.PreviousStateId, result.StateId, result.History)
                : WorldCommitResult.Unchanged(result.StateId);
        }
        catch (OptimisticConcurrencyConflictException exception)
        {
            throw new WorldStateConflictException(exception.ExpectedStateId, exception.ActualStateId);
        }
        catch (WorldTransactionException)
        {
            CancelPendingAutoSave();
            throw;
        }
    }

    private void PublishCommit(WorldCommitResult result, WorldSessionOperation operation)
    {
        if (!result.Changed)
            return;
        ScheduleAutoSave();
        Changed?.Invoke(this, new WorldSessionChangedEventArgs(result.CommitId, result.StateId, operation, result.ChangeSet!));
        NotifyDirtyChanged();
    }

    private async Task SaveCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            (WorldSnapshot Snapshot, Guid StateId, bool IsSaved) state = ExecuteQuery(world => (world.CreateSnapshot(), world.StateId, world.StateId == _savedStateId));
            if (!force && state.IsSaved)
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
            _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = state.StateId;
                return true;
            });
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
            _autoSaveTasks.Add(RunAutoSaveAsync(source));
        }
    }

    private async Task RunAutoSaveAsync(CancellationTokenSource source)
    {
        await Task.Yield();
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

    private async Task CancelAndDrainPendingAutoSaveAsync()
    {
        Task[] tasks;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource = null;
            tasks = _autoSaveTasks.ToArray();
            _autoSaveTasks.Clear();
        }
        if (tasks.Length > 0)
            await Task.WhenAll(tasks);
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

    private void ValidateRegisteredTypes(IEnumerable<WorldOperation> operations)
    {
        foreach (WorldOperation operation in operations)
            ValidateRegisteredType(operation);
    }

    private void ValidateRegisteredType(WorldOperation operation)
    {
        string? unregistered = operation switch
        {
            AddElementOperation value when !_typePolicy.IsRegistered(value.Type) => $"ElementType {value.Type}",
            UpdateElementTypeOperation value when !_typePolicy.IsRegistered(value.Type) => $"ElementType {value.Type}",
            AddScopeOperation value when !_typePolicy.IsRegistered(value.Type) => $"ScopeType {value.Type}",
            UpdateScopeTypeOperation value when !_typePolicy.IsRegistered(value.Type) => $"ScopeType {value.Type}",
            AddAspectOperation value when !_typePolicy.IsRegistered(value.Type) => $"AspectType {value.Type}",
            UpdateAspectTypeOperation value when !_typePolicy.IsRegistered(value.Type) => $"AspectType {value.Type}",
            AddRelationOperation value when !_typePolicy.IsRegistered(value.Type) => $"RelationType {value.Type}",
            UpdateRelationTypeOperation value when !_typePolicy.IsRegistered(value.Type) => $"RelationType {value.Type}",
            _ => null
        };
        if (unregistered is not null)
            throw new ArgumentException($"World 操作 {operation.GetType().Name} 使用了未注册或类别不匹配的 {unregistered}。", nameof(operation));
    }

    private TResult ExecuteLocked<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        return _workspace.ExecuteExclusive(operation);
    }

    private void EnsureUsable()
    {
        ThrowIfDisposed();
        _workspace.Read(_ => true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

}
