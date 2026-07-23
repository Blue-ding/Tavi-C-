using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>
/// 持有当前运行时世界，并协调访问、变化通知、加载和自动保存。
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
    }

    /// <summary>
    /// 在当前运行时世界发生实际修改后触发。
    /// </summary>
    public event EventHandler<WorldSessionChangedEventArgs>? Changed;

    /// <summary>
    /// 在脏状态或保存状态发生变化后触发。
    /// </summary>
    public event EventHandler<WorldSessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 获取当前运行时世界；会话初始化前访问会抛出异常。
    /// </summary>
    public RuntimeWorld Current
    {
        get
        {
            ThrowIfDisposed();
            return _current ?? throw new InvalidOperationException("WorldSession 尚未初始化。");
        }
    }

    /// <summary>
    /// 获取会话是否包含尚未保存的修改。
    /// </summary>
    public bool IsDirty => Read(world => world.Revision != Volatile.Read(ref _savedRevision));

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
        if (_current is not null)
            throw new InvalidOperationException("WorldSession 已经初始化。");
        WorldSnapshot? snapshot = await _store.LoadAsync(_slot, cancellationToken);
        AttachWorld(RuntimeWorld.Create(snapshot ?? new WorldSnapshot()));
        Volatile.Write(ref _savedRevision, snapshot is null ? -1 : Current.Revision);
        NotifyDirtyChanged();
        if (snapshot is null)
            ScheduleAutoSave();
    }

    /// <summary>
    /// 在会话同步边界内读取当前运行时世界。
    /// </summary>
    public TResult Read<TResult>(Func<RuntimeWorld, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();
        lock (_worldSync)
            return query(Current);
    }

    /// <summary>
    /// 在会话同步边界内执行可能修改当前运行时世界的操作。
    /// </summary>
    public void Update(Action<RuntimeWorld> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        lock (_worldSync)
            operation(Current);
    }

    /// <summary>
    /// 在会话同步边界内执行可能修改当前运行时世界的操作并返回结果。
    /// </summary>
    public TResult Update<TResult>(Func<RuntimeWorld, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        lock (_worldSync)
            return operation(Current);
    }

    /// <summary>
    /// 立即保存当前世界，无论当前是否为脏状态。
    /// </summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _ = Current;
        CancelPendingAutoSave();
        return SaveCoreAsync(true, cancellationToken);
    }

    /// <summary>
    /// 如果当前世界包含未保存修改，则立即写入存档。
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _ = Current;
        CancelPendingAutoSave();
        return SaveCoreAsync(false, cancellationToken);
    }

    /// <summary>
    /// 取消自动保存并在释放会话前刷新未保存修改。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        CancelPendingAutoSave();
        try
        {
            if (_current is not null)
                await SaveCoreAsync(false, CancellationToken.None);
        }
        finally
        {
            if (_current is not null)
                _current.Changed -= OnWorldChanged;
            _disposed = true;
            _lifetimeSource.Cancel();
            _lifetimeSource.Dispose();
            _saveGate.Dispose();
        }
    }

    private void AttachWorld(RuntimeWorld world)
    {
        lock (_worldSync)
        {
            if (_current is not null)
                _current.Changed -= OnWorldChanged;
            _current = world;
            _current.Changed += OnWorldChanged;
        }
    }

    private void OnWorldChanged(object? sender, WorldChangedEventArgs eventArgs)
    {
        ScheduleAutoSave();
        Changed?.Invoke(this, new WorldSessionChangedEventArgs(eventArgs.Revision, eventArgs.Operation));
        NotifyDirtyChanged();
    }

    private async Task SaveCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            (WorldSnapshot Snapshot, long Revision) state = Read(world => (world.CreateSnapshot(), world.Revision));
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
            StateChanged?.Invoke(
                this,
                new WorldSessionStateChangedEventArgs(
                    WorldSessionStateChange.DirtyChanged,
                    isDirty
                )
            );
    }

    private void RaiseStateChanged(
        WorldSessionStateChange change,
        Exception? exception = null)
    {
        StateChanged?.Invoke(
            this,
            new WorldSessionStateChangedEventArgs(change, IsDirty, exception)
        );
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
