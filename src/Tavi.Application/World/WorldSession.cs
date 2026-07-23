using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>
/// 持有当前世界，并协调加载、脏标记、防抖自动保存和退出刷新。
/// </summary>
public sealed class WorldSession : IAsyncDisposable
{
    private readonly IWorldStore _store;
    private readonly string _slot;
    private readonly TimeSpan _autoSaveDelay;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _debounceSync = new();
    private readonly CancellationTokenSource _lifetimeSource = new();
    private CancellationTokenSource? _debounceSource;
    private WorldGraph? _current;
    private long _changeVersion;
    private long _savedVersion;
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
    /// 获取当前世界；会话初始化前访问会抛出异常。
    /// </summary>
    public WorldGraph Current => _current ?? throw new InvalidOperationException("WorldSession 尚未初始化。");

    /// <summary>
    /// 获取会话是否包含尚未保存的修改。
    /// </summary>
    public bool IsDirty => Volatile.Read(ref _changeVersion) != Volatile.Read(ref _savedVersion);

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
        _current = WorldGraph.Create(snapshot ?? new WorldSnapshot());
        if (snapshot is null)
        {
            Interlocked.Increment(ref _changeVersion);
            ScheduleAutoSave();
        }
    }

    /// <summary>
    /// 执行一次世界修改，并在操作成功后标记为待保存。
    /// </summary>
    public void Update(Action<WorldGraph> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ThrowIfDisposed();
        update(Current);
        MarkDirty();
    }

    /// <summary>
    /// 将当前世界标记为待保存，并重新安排防抖自动保存。
    /// </summary>
    public void MarkDirty()
    {
        ThrowIfDisposed();
        _ = Current;
        Interlocked.Increment(ref _changeVersion);
        ScheduleAutoSave();
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
            _disposed = true;
            _lifetimeSource.Cancel();
            _lifetimeSource.Dispose();
            _saveGate.Dispose();
        }
    }

    private async Task SaveCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            long targetVersion = Volatile.Read(ref _changeVersion);
            if (!force && targetVersion == Volatile.Read(ref _savedVersion))
                return;
            await _store.SaveAsync(_slot, Current.CreateSnapshot(), cancellationToken);
            Volatile.Write(ref _savedVersion, targetVersion);
            LastAutoSaveException = null;
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
