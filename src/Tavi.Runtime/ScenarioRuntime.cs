using Tavi.Application.Scenario;
using Tavi.Domain.World;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>持有当前独立 Scenario 会话，并为 Host 请求提供串行访问和持久化生命周期。</summary>
public sealed class ScenarioRuntime : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ExtensionRuntime _extensions;
    private readonly WorldRuntime _world;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly object _disposeSync = new();
    private JsonFileScenarioStore? _store;
    private IScenarioService? _service;
    private Task? _disposeTask;
    private bool _disposed;

    /// <summary>创建依赖当前 World 与冻结 Module Runtime 的 Scenario 运行时。</summary>
    public ScenarioRuntime(IConfiguration configuration, ExtensionRuntime extensions, WorldRuntime world)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>从当前 World 快照创建或恢复默认 Scenario 会话。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string saveDirectory = _configuration["Tavi:ScenarioDirectory"] ?? _configuration["Tavi:SaveDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_SCENARIO_DIRECTORY") ?? defaultDirectory;
        string slot = _configuration["Tavi:ScenarioSlot"] ?? "default";
        WorldSnapshot world = await _world.ExecuteAsync(service => service.Queries.CreateSnapshot(), cancellationToken);
        _store = new JsonFileScenarioStore(saveDirectory);
        _service = new ScenarioSession(_store, _extensions.Frozen, world, slot);
        await _service.InitializeAsync(cancellationToken);
    }

    /// <summary>停止运行时并刷新尚未保存的 Scenario。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在运行时访问锁内执行同步 Scenario 操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IScenarioService, TResult> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return operation(RequireService());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>在运行时访问锁内执行异步 Scenario 操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IScenarioService, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(RequireService());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>释放 Scenario 会话、存储与访问同步资源。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        if (_service is not null)
            await _service.DisposeAsync();
        _store?.Dispose();
        _accessGate.Dispose();
    }

    private IScenarioService RequireService() => _service ?? throw new InvalidOperationException("Scenario 运行时尚未初始化。");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
