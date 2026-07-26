using Tavi.Application.Performance;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>持有至多一个活动 PerformanceSession，并管理恢复、串行访问、归档和关闭刷新。</summary>
public sealed class PerformanceRuntime : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ExtensionRuntime _extensions;
    private readonly WritingRuntime _writing;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly object _disposeSync = new();
    private JsonFilePerformanceStore? _store;
    private IPerformanceWorkspace? _workspace;
    private IPerformanceSessionLifecycle? _lifecycle;
    private Task? _disposeTask;
    private bool _disposed;

    public PerformanceRuntime(IConfiguration configuration, ExtensionRuntime extensions, WritingRuntime writing)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _writing = writing ?? throw new ArgumentNullException(nameof(writing));
    }

    public bool HasSession => _workspace is not null;
    public bool HasActivePerformance => _workspace?.Status == PerformanceStatus.Active;

    /// <summary>创建 Store，并在存在唯一活动记录时恢复 PerformanceSession。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Performances");
        string directory = _configuration["Tavi:PerformanceDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_PERFORMANCE_DIRECTORY") ?? defaultDirectory;
        int delayMilliseconds = int.TryParse(_configuration["Tavi:Performance:AutoSaveDelayMilliseconds"], out int delay) && delay >= 0 ? delay : 1000;
        _store = new JsonFilePerformanceStore(directory);
        if (await _store.LoadActiveAsync(cancellationToken) is null)
            return;
        var session = new PerformanceSession(_store, _writing.BeatPublisher, _extensions.Frozen, TimeSpan.FromMilliseconds(delayMilliseconds));
        await session.InitializeAsync(cancellationToken);
        Attach(session);
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在当前没有 PerformanceSession 时，从冻结 Processing Scene 创建唯一活动 Performance。</summary>
    public async Task<PerformanceSnapshot> StartPerformanceAsync(SceneContextView scene, long randomSeed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            if (_workspace is not null)
                throw new InvalidOperationException($"已有 Performance {_workspace.Id} 占用唯一会话席位。");
            JsonFilePerformanceStore store = RequireStore();
            int delayMilliseconds = int.TryParse(_configuration["Tavi:Performance:AutoSaveDelayMilliseconds"], out int delay) && delay >= 0 ? delay : 1000;
            var session = new PerformanceSession(store, scene, randomSeed, _writing.BeatPublisher, _extensions.Frozen, TimeSpan.FromMilliseconds(delayMilliseconds));
            try
            {
                await session.InitializeAsync(cancellationToken);
                Attach(session);
                return session.Queries.CreateSnapshot();
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<IPerformanceWorkspace, TResult> operation, CancellationToken cancellationToken = default)
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

    public async Task<TResult> ExecuteAsync<TResult>(Func<IPerformanceWorkspace, Task<TResult>> operation, CancellationToken cancellationToken = default)
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

    /// <summary>归档已经 Completed 或 Abandoned 的 Performance，并释放唯一会话席位。</summary>
    public async Task<PerformanceSnapshot> ArchiveEndedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            IPerformanceWorkspace workspace = RequireWorkspace();
            if (workspace.Status == PerformanceStatus.Active)
                throw new InvalidOperationException("活动 Performance 不能归档。");
            await workspace.Commands.SaveAsync(cancellationToken);
            PerformanceSnapshot snapshot = workspace.Queries.CreateSnapshot();
            await RequireStore().ArchiveAsync(snapshot, cancellationToken);
            await DetachAsync();
            return snapshot;
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        await _accessGate.WaitAsync();
        try
        {
            await DetachAsync();
            _store?.Dispose();
        }
        finally
        {
            _accessGate.Release();
            _accessGate.Dispose();
        }
    }

    private void Attach(PerformanceSession session)
    {
        _workspace = session;
        _lifecycle = session;
    }

    private async Task DetachAsync()
    {
        if (_lifecycle is not null)
            await _lifecycle.DisposeAsync();
        _workspace = null;
        _lifecycle = null;
    }

    private JsonFilePerformanceStore RequireStore() => _store ?? throw new InvalidOperationException("Performance 运行时尚未初始化。");
    private IPerformanceWorkspace RequireWorkspace() => _workspace ?? throw new InvalidOperationException("当前没有 Performance 会话。");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
