using Tavi.Application.World;
using Tavi.Host.ViewModels;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Host.Runtime;

/// <summary>持有当前世界会话，并为所有展示层读写提供统一的串行访问和生命周期边界。</summary>
public sealed class WorldRuntime : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly WorldEventBroker _events;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private JsonFileWorldStore? _store;
    private WorldSession? _session;
    private bool _disposed;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    /// <summary>创建使用指定配置和事件代理的世界运行时。</summary>
    public WorldRuntime(IConfiguration configuration, WorldEventBroker events)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>初始化默认 JSON 存储和世界会话。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string saveDirectory = _configuration["Tavi:SaveDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_SAVE_DIRECTORY") ?? defaultDirectory;
        string slot = _configuration["Tavi:WorldSlot"] ?? "default";
        _store = new JsonFileWorldStore(saveDirectory);
        _session = new WorldSession(_store, slot);
        _session.Changed += OnWorldChanged;
        _session.StateChanged += OnWorldStateChanged;
        await _session.InitializeAsync(cancellationToken);
    }

    /// <summary>停止运行时并刷新尚未保存的世界修改。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在运行时访问锁内执行同步世界操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<WorldSession, TResult> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return operation(RequireSession());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>在运行时访问锁内执行异步世界操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<WorldSession, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(RequireSession());
        }
        finally
        {
            _accessGate.Release();
        }
    }

    internal WorldSession Session => RequireSession();

    /// <summary>释放世界会话、存储和访问同步资源。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        if (_session is not null)
        {
            _session.Changed -= OnWorldChanged;
            _session.StateChanged -= OnWorldStateChanged;
            await _session.DisposeAsync();
        }
        _store?.Dispose();
        _accessGate.Dispose();
    }

    private void OnWorldChanged(object? sender, WorldSessionChangedEventArgs eventArgs)
    {
        WorldSession session = RequireSession();
        _events.Publish(new WorldEventViewModel("world.changed", eventArgs.Revision, session.IsDirty, eventArgs.CommitId, eventArgs.Operation.ToString(), null));
    }

    private void OnWorldStateChanged(object? sender, WorldSessionStateChangedEventArgs eventArgs)
    {
        long revision = _session?.Revision ?? 0;
        _events.Publish(new WorldEventViewModel($"world.{ToKebabCase(eventArgs.Change.ToString())}", revision, eventArgs.IsDirty, null, null, eventArgs.Exception?.Message));
    }

    private WorldSession RequireSession() => _session ?? throw new InvalidOperationException("世界运行时尚未初始化。");

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
