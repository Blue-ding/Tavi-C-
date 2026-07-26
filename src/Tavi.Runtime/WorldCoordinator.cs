using Tavi.Application.World;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>持有唯一 WorldBuildSession，并按读取、提案和审批权限协调所有运行期访问。</summary>
public sealed class WorldCoordinator : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly WorldEventBroker _events;
    private readonly ExtensionRuntime _extensions;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private JsonFileWorldStore? _store;
    private IWorldBuildWorkspace? _workspace;
    private IWorldBuildSessionLifecycle? _lifecycle;
    private bool _disposed;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    /// <summary>创建使用指定配置和事件代理的世界运行时。</summary>
    public WorldCoordinator(IConfiguration configuration, WorldEventBroker events, ExtensionRuntime extensions)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
    }

    /// <summary>初始化默认 JSON 存储和世界会话。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string saveDirectory = _configuration["Tavi:SaveDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_SAVE_DIRECTORY") ?? defaultDirectory;
        string slot = _configuration["Tavi:WorldSlot"] ?? "default";
        _store = new JsonFileWorldStore(saveDirectory);
        var session = new WorldBuildSession(_store, _extensions.Frozen.Catalog, slot);
        _workspace = session;
        _lifecycle = session;
        _lifecycle.Changed += OnWorldChanged;
        _lifecycle.StateChanged += OnWorldStateChanged;
        await _lifecycle.InitializeAsync(cancellationToken);
    }

    /// <summary>停止运行时并刷新尚未保存的世界修改。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在协调锁内执行只读操作。</summary>
    public Task<TResult> ReadAsync<TResult>(Func<IWorldBuildView, TResult> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    /// <summary>在协调锁内执行异步只读操作。</summary>
    public Task<TResult> ReadAsync<TResult>(Func<IWorldBuildView, Task<TResult>> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    /// <summary>在协调锁内执行只能提出修改的操作。</summary>
    public Task<TResult> ContributeAsync<TResult>(Func<IWorldBuildContributor, TResult> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    /// <summary>在协调锁内执行异步提案操作。</summary>
    public Task<TResult> ContributeAsync<TResult>(Func<IWorldBuildContributor, Task<TResult>> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    /// <summary>在协调锁内执行玩家审批、历史或保存操作。</summary>
    public Task<TResult> ControlAsync<TResult>(Func<IWorldBuildController, TResult> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    /// <summary>在协调锁内执行异步玩家控制操作。</summary>
    public Task<TResult> ControlAsync<TResult>(Func<IWorldBuildController, Task<TResult>> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    private async Task<TResult> ExecuteAsync<TCapability, TResult>(Func<TCapability, TResult> operation, CancellationToken cancellationToken)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return operation(RequireCapability<TCapability>());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    private async Task<TResult> ExecuteAsync<TCapability, TResult>(Func<TCapability, Task<TResult>> operation, CancellationToken cancellationToken)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(RequireCapability<TCapability>());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>获取已初始化的 World Application 工作区。</summary>
    internal IWorldBuildView View => RequireWorkspace();
    internal IWorldBuildContributor Contributor => RequireWorkspace();
    internal IWorldBuildController Controller => RequireWorkspace();

    /// <summary>释放世界会话、存储和访问同步资源。</summary>
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
            _lifecycle.Changed -= OnWorldChanged;
            _lifecycle.StateChanged -= OnWorldStateChanged;
            await _lifecycle.DisposeAsync();
        }
        _store?.Dispose();
        _accessGate.Dispose();
    }

    private void OnWorldChanged(object? sender, WorldBuildChangedEventArgs eventArgs)
    {
        IWorldBuildWorkspace workspace = RequireWorkspace();
        _events.Publish(new WorldRuntimeEvent("world.changed", eventArgs.StateId, workspace.IsDirty, eventArgs.CommitId, eventArgs.Operation.ToString(), null));
    }

    private void OnWorldStateChanged(object? sender, WorldBuildStateChangedEventArgs eventArgs)
    {
        Guid stateId = _workspace?.StateId ?? Guid.Empty;
        _events.Publish(new WorldRuntimeEvent($"world.{ToKebabCase(eventArgs.Change.ToString())}", stateId, eventArgs.IsDirty, null, null, eventArgs.Exception?.Message));
    }

    private IWorldBuildWorkspace RequireWorkspace() => _workspace ?? throw new InvalidOperationException("世界运行时尚未初始化。");

    private TCapability RequireCapability<TCapability>() where TCapability : class
        => RequireWorkspace() as TCapability
           ?? throw new InvalidOperationException($"WorldBuildSession 不提供 {typeof(TCapability).Name} 权限。");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string ToKebabCase(string value)
    {
        var characters = new List<char>(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character))
                characters.Add('-');
            characters.Add(char.ToLowerInvariant(character));
        }
        return new string(characters.ToArray());
    }
}
