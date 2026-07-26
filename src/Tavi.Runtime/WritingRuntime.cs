using Tavi.Application;
using Tavi.Application.Writing;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>持有唯一 WritingSession，并为 Host 请求提供串行访问、配置和关闭刷新边界。</summary>
public sealed class WritingRuntime : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly WritingEventBroker _events;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly object _disposeSync = new();
    private JsonFileManuscriptStore? _store;
    private IWritingWorkspace? _workspace;
    private IWritingSessionLifecycle? _lifecycle;
    private IBeatPublisher? _beatPublisher;
    private Task? _disposeTask;
    private bool _disposed;

    /// <summary>创建使用指定 Host 配置的 Writing 运行时。</summary>
    public WritingRuntime(
        IConfiguration configuration,
        WritingEventBroker events)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>创建专用手稿存储并恢复上次未归档的活动手稿。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Manuscripts");
        string directory = _configuration["Tavi:ManuscriptDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_MANUSCRIPT_DIRECTORY") ?? defaultDirectory;
        bool autoSaveEnabled = !bool.TryParse(_configuration["Tavi:Writing:AutoSave"], out bool configured) || configured;
        int delayMilliseconds = int.TryParse(_configuration["Tavi:Writing:AutoSaveDelayMilliseconds"], out int delay) && delay >= 0 ? delay : 1500;
        _store = new JsonFileManuscriptStore(directory);
        var session = new WritingSession(_store, autoSaveEnabled ? TimeSpan.FromMilliseconds(delayMilliseconds) : null);
        await session.InitializeAsync(cancellationToken);
        _workspace = session;
        _lifecycle = session;
        _beatPublisher = session;
        _lifecycle.Changed += OnWritingChanged;
        _lifecycle.StateChanged += OnWritingStateChanged;
    }

    /// <summary>停止运行时并刷新未保存的活动手稿。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在运行时访问锁内执行同步 Writing 服务操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IWritingWorkspace, TResult> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return operation(RequireWorkspace());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>在运行时访问锁内执行异步 Writing 服务操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IWritingWorkspace, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(RequireWorkspace());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>向同一 Runtime 内的 Performance 暴露最小 Beat 发布能力。</summary>
    internal IBeatPublisher BeatPublisher => _beatPublisher ?? throw new InvalidOperationException("Writing 运行时尚未初始化。");

    /// <summary>释放会话、文件存储和运行时访问锁。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        if (_lifecycle is not null)
        {
            _lifecycle.Changed -= OnWritingChanged;
            _lifecycle.StateChanged -= OnWritingStateChanged;
            await _lifecycle.DisposeAsync();
        }
        _store?.Dispose();
        _accessGate.Dispose();
    }
    private IWritingWorkspace RequireWorkspace() => _workspace ?? throw new InvalidOperationException("Writing 运行时尚未初始化。");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void OnWritingChanged(
        object? sender,
        WritingSessionChangedEventArgs eventArgs)
    {
        WritingSnapshot snapshot = RequireWorkspace().Queries.CreateSnapshot();
        _events.Publish(new WritingRuntimeEvent(
            "writing.changed",
            eventArgs.Commit.StateId,
            snapshot.IsDirty,
            eventArgs.Commit.CommitId,
            eventArgs.Operation,
            null));
    }

    private void OnWritingStateChanged(
        object? sender,
        SessionStateChangedEventArgs eventArgs)
    {
        Guid stateId = _workspace?.Queries.CreateSnapshot().Manuscript?.StateId
            ?? Guid.Empty;
        _events.Publish(new WritingRuntimeEvent(
            $"writing.{RuntimeEventNames.ToKebabCase(eventArgs.Change.ToString())}",
            stateId,
            eventArgs.IsDirty,
            null,
            null,
            eventArgs.Exception?.Message));
    }
}
