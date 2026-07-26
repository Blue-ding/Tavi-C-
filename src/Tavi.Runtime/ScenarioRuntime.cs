using Tavi.Application;
using Tavi.Application.Scenario;
using Tavi.Domain.Scenario;
using Tavi.Domain.World;
using Tavi.Infrastructure.Persistence;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Runtime;

/// <summary>持有当前独立 Scenario 会话，并为 Host 请求提供串行访问和持久化生命周期。</summary>
public sealed class ScenarioRuntime : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ExtensionRuntime _extensions;
    private readonly WorldRuntime _world;
    private readonly ScenarioEventBroker _events;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly object _disposeSync = new();
    private JsonFileScenarioStore? _store;
    private string _slot = "default";
    private IScenarioWorkspace? _workspace;
    private IScenarioSessionLifecycle? _lifecycle;
    private Task? _disposeTask;
    private bool _disposed;

    /// <summary>创建依赖当前 World 与冻结 Module Runtime 的 Scenario 运行时。</summary>
    public ScenarioRuntime(
        IConfiguration configuration,
        ExtensionRuntime extensions,
        WorldRuntime world,
        ScenarioEventBroker events)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>从当前 World 快照创建或恢复默认 Scenario 会话。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string saveDirectory = _configuration["Tavi:ScenarioDirectory"] ?? _configuration["Tavi:SaveDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_SCENARIO_DIRECTORY") ?? defaultDirectory;
        _slot = _configuration["Tavi:ScenarioSlot"] ?? "default";
        WorldSnapshot world = await _world.ReadAsync(workspace => workspace.Queries.CreateSnapshot(), cancellationToken);
        _store = new JsonFileScenarioStore(saveDirectory);
        var session = new ScenarioSession(_store, _extensions.Frozen, world, _slot);
        Attach(session);
        try
        {
            await session.InitializeAsync(cancellationToken);
        }
        catch
        {
            DetachEvents(session);
            _workspace = null;
            _lifecycle = null;
            await session.DiscardAsync();
            throw;
        }
    }

    /// <summary>停止运行时并刷新尚未保存的 Scenario。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>在运行时访问锁内执行同步 Scenario 操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IScenarioWorkspace, TResult> operation, CancellationToken cancellationToken = default)
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

    /// <summary>在运行时访问锁内执行异步 Scenario 操作。</summary>
    public async Task<TResult> ExecuteAsync<TResult>(Func<IScenarioWorkspace, Task<TResult>> operation, CancellationToken cancellationToken = default)
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

    /// <summary>在当前 Scenario 没有 Scene 时，用指定 World 快照显式重建它。</summary>
    public async Task<ScenarioSnapshot> ReplaceFromWorldAsync(WorldSnapshot world, Guid expectedScenarioStateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ThrowIfDisposed();
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            IScenarioWorkspace current = RequireWorkspace();
            if (current.StateId != expectedScenarioStateId)
                throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(ReplaceFromWorldAsync), $"Scenario 状态冲突：期望 {expectedScenarioStateId}，实际 {current.StateId}。");
            if (current.Queries.GetScenes().Count > 0)
                throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, nameof(ReplaceFromWorldAsync), "Scenario 仍包含 Scene；请先完成回写并清理 Scene，或显式放弃当前演绎。");

            ScenarioSnapshot imported = ScenarioWorldBridge.Import(world, _extensions.Frozen.Catalog, _extensions.Frozen.Parameters);
            var candidate = new ScenarioSession(new SeededScenarioStore(RequireStore(), imported), _extensions.Frozen, world.Id, _slot);
            await candidate.InitializeAsync(cancellationToken);
            IScenarioSessionLifecycle lifecycle = RequireLifecycle();
            DetachEvents(lifecycle);
            await lifecycle.DiscardAsync();
            _workspace = null;
            _lifecycle = null;
            try
            {
                await candidate.Commands.SaveAsync(cancellationToken);
                Attach(candidate);
                return candidate.Queries.CreateSnapshot();
            }
            catch
            {
                await candidate.DiscardAsync();
                await RestorePersistedAsync(cancellationToken);
                throw;
            }
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
        if (_lifecycle is not null)
        {
            DetachEvents(_lifecycle);
            await _lifecycle.DisposeAsync();
        }
        _store?.Dispose();
        _accessGate.Dispose();
    }

    private void Attach(ScenarioSession session)
    {
        _workspace = session;
        _lifecycle = session;
        _lifecycle.Changed += OnScenarioChanged;
        _lifecycle.StateChanged += OnScenarioStateChanged;
    }

    private void DetachEvents(IScenarioSessionLifecycle lifecycle)
    {
        lifecycle.Changed -= OnScenarioChanged;
        lifecycle.StateChanged -= OnScenarioStateChanged;
    }

    private void OnScenarioChanged(
        object? sender,
        ScenarioSessionChangedEventArgs eventArgs)
    {
        IScenarioWorkspace workspace = RequireWorkspace();
        _events.Publish(new ScenarioRuntimeEvent(
            "scenario.changed",
            eventArgs.Commit.StateId,
            workspace.IsDirty,
            eventArgs.Commit.CommitId,
            eventArgs.Operation,
            null));
    }

    private void OnScenarioStateChanged(
        object? sender,
        SessionStateChangedEventArgs eventArgs)
    {
        Guid stateId = _workspace?.StateId ?? Guid.Empty;
        _events.Publish(new ScenarioRuntimeEvent(
            $"scenario.{RuntimeEventNames.ToKebabCase(eventArgs.Change.ToString())}",
            stateId,
            eventArgs.IsDirty,
            null,
            null,
            eventArgs.Exception?.Message));
    }

    private async Task RestorePersistedAsync(CancellationToken cancellationToken)
    {
        ScenarioSnapshot? persisted = await RequireStore().LoadAsync(_slot, cancellationToken);
        if (persisted is null)
            throw new InvalidOperationException("Scenario 替换失败后无法恢复原存档。");
        var restored = new ScenarioSession(RequireStore(), _extensions.Frozen, persisted.SourceWorldStateId, _slot);
        await restored.InitializeAsync(cancellationToken);
        Attach(restored);
    }

    private JsonFileScenarioStore RequireStore() => _store ?? throw new InvalidOperationException("Scenario 运行时尚未初始化。");
    private IScenarioSessionLifecycle RequireLifecycle() => _lifecycle ?? throw new InvalidOperationException("Scenario 运行时尚未初始化。");
    private IScenarioWorkspace RequireWorkspace() => _workspace ?? throw new InvalidOperationException("Scenario 运行时尚未初始化。");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class SeededScenarioStore(IScenarioStore inner, ScenarioSnapshot seed) : IScenarioStore
    {
        private readonly ScenarioSnapshot _seed = RuntimeScenario.Create(seed).CreateSnapshot();

        public Task<ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
            => Task.FromResult<ScenarioSnapshot?>(RuntimeScenario.Create(_seed).CreateSnapshot());

        public Task SaveAsync(string slot, ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
            => inner.SaveAsync(slot, snapshot, cancellationToken);
    }
}
