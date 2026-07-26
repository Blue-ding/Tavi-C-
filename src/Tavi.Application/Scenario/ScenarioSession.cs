using Tavi.Domain.Scenario;
using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Scenario;
using System.Collections.ObjectModel;
using Tavi.Extensibility;
using Tavi.Utilities.Concurrency;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;
using WorldSnapshot = Tavi.Domain.World.WorldSnapshot;

namespace Tavi.Application.Scenario;

/// <summary>独占当前 Scenario，并统一控制 Module 查询、Scene 生命周期、局部结算、历史和持久化。</summary>
public sealed class ScenarioSession : IScenarioWorkspace, IScenarioSessionLifecycle
{
    private readonly IScenarioStore _store;
    private readonly ModuleCatalog _catalog;
    private readonly FrozenModuleRuntime _extensions;
    private readonly ScenarioSemanticValidator _validator;
    private readonly Guid _sourceWorldStateId;
    private readonly ScenarioSnapshot? _initialSnapshot;
    private IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> _moduleParameters = new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>();
    private readonly string _slot;
    private readonly TimeSpan _autoSaveDelay;
    private readonly object _debounceSync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly VersionedWorkspace<RuntimeScenario, ScenarioChangeSet, AppliedScenarioChangeSet> _workspace;
    private readonly List<Task> _autoSaveTasks = [];
    private CancellationTokenSource? _debounceSource;
    private Guid _savedStateId;
    private bool _disposed;

    /// <summary>创建持有独立 Scenario 的 ScenarioSession。</summary>
    public ScenarioSession(IScenarioStore store, ModuleCatalog catalog, Guid sourceWorldStateId, string slot = "default", TimeSpan? autoSaveDelay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _extensions = new FrozenModuleRuntime(catalog, new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>(), []);
        if (sourceWorldStateId == Guid.Empty)
            throw new ArgumentException("Source World StateId 不能为空。", nameof(sourceWorldStateId));
        if (string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("Scenario 存档槽不能为空。", nameof(slot));
        _sourceWorldStateId = sourceWorldStateId;
        _slot = slot;
        _autoSaveDelay = autoSaveDelay ?? TimeSpan.FromSeconds(1);
        if (_autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay), "自动保存延迟不能为负数。");
        _validator = new ScenarioSemanticValidator(catalog);
        _workspace = new VersionedWorkspace<RuntimeScenario, ScenarioChangeSet, AppliedScenarioChangeSet>(new ScenarioConcurrencyModel(_validator));
        Queries = new ScenarioQueries(this);
        Commands = new ScenarioCommands(this);
    }

    /// <summary>创建会在存档不存在时吸收完整 World 快照的 ScenarioSession。</summary>
    public ScenarioSession(IScenarioStore store, ModuleCatalog catalog, WorldSnapshot sourceWorld, string slot = "default", TimeSpan? autoSaveDelay = null)
        : this(store, catalog, sourceWorld?.Id ?? throw new ArgumentNullException(nameof(sourceWorld)), slot, autoSaveDelay)
    {
        _initialSnapshot = ScenarioWorldBridge.Import(sourceWorld, catalog);
    }

    /// <summary>创建使用冻结 Extension 快照的新 ScenarioSession。</summary>
    public ScenarioSession(IScenarioStore store, FrozenModuleRuntime extensions, Guid sourceWorldStateId, string slot = "default", TimeSpan? autoSaveDelay = null)
        : this(store, extensions?.Catalog ?? throw new ArgumentNullException(nameof(extensions)), sourceWorldStateId, slot, autoSaveDelay)
    {
        _extensions = extensions;
        _moduleParameters = CopyParameters(extensions.Parameters);
    }

    /// <summary>创建会使用冻结 Extension 快照吸收完整 World 的 ScenarioSession。</summary>
    public ScenarioSession(IScenarioStore store, FrozenModuleRuntime extensions, WorldSnapshot sourceWorld, string slot = "default", TimeSpan? autoSaveDelay = null)
        : this(store, extensions?.Catalog ?? throw new ArgumentNullException(nameof(extensions)), sourceWorld, slot, autoSaveDelay)
    {
        _extensions = extensions;
        _moduleParameters = CopyParameters(extensions.Parameters);
        _initialSnapshot = ScenarioWorldBridge.Import(sourceWorld, extensions.Catalog, _moduleParameters);
    }

    /// <inheritdoc />
    public event EventHandler<ScenarioSessionChangedEventArgs>? Changed;

    /// <inheritdoc />
    public ScenarioQueries Queries { get; }

    /// <inheritdoc />
    public ScenarioCommands Commands { get; }

    /// <inheritdoc />
    public ScenarioSessionHealth Health => _workspace.Health == VersionedWorkspaceHealth.Healthy ? ScenarioSessionHealth.Healthy : ScenarioSessionHealth.Faulted;

    /// <inheritdoc />
    public Guid StateId => ExecuteQuery(scenario => scenario.StateId);

    /// <inheritdoc />
    public bool IsDirty => ExecuteQuery(scenario => scenario.StateId != _savedStateId);

    /// <inheritdoc />
    public bool CanUndo => _workspace.CanUndo;

    /// <inheritdoc />
    public bool CanRedo => _workspace.CanRedo;

    /// <inheritdoc />
    public Exception? LastAutoSaveException { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_workspace.IsInitialized)
            throw InvalidState(nameof(InitializeAsync), "ScenarioSession 已经初始化。");
        ScenarioSnapshot? loaded = await _store.LoadAsync(_slot, cancellationToken);
        ScenarioSnapshot snapshot = loaded ?? CreateInitialSnapshot();
        EnsureFrozenParametersMatch(snapshot);
        RuntimeScenario scenario = RuntimeScenario.Create(snapshot);
        _validator.EnsureValid(scenario.CreateSnapshot(), nameof(InitializeAsync));
        try
        {
            _workspace.Initialize(scenario);
        }
        catch (InvalidOperationException exception) when (_workspace.IsInitialized)
        {
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, nameof(InitializeAsync), "ScenarioSession 已经初始化。", innerException: exception);
        }
        _workspace.ExecuteExclusive(() =>
        {
            _savedStateId = loaded is null ? Guid.Empty : scenario.StateId;
            return true;
        });
    }

    /// <inheritdoc />
    internal ScenarioCommitResult Apply(ScenarioChangeSet changeSet, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.Operations.Any(operation => operation is AddSceneOperation or RemoveSceneOperation or SetSceneSlotBindingOperation or ClearSceneSlotBindingOperation or UpdateSceneStateOperation or ClearSettledScenesOperation))
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.Protocol, nameof(Apply), "Scene 生命周期操作必须通过 ScenarioSession 的专用方法提交。");
        return Commit(changeSet, expectedStateId);
    }

    private ScenarioCommitResult Commit(ScenarioChangeSet changeSet, Guid expectedStateId)
    {
        ScenarioCommitResult result = ExecuteWorkspace(() => _workspace.Commit(changeSet, expectedStateId), nameof(Apply));
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    internal async Task<IReadOnlyList<SceneDefinition>> GetSceneDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default)
    {
        ScenarioSnapshot snapshot = ExecuteQuery(scenario => scenario.CreateSnapshot());
        Dictionary<string, string> modules = snapshot.Modules.ToDictionary(value => value.Id, value => value.Version, StringComparer.Ordinal);
        var definitions = _catalog.StaticScenes.Where(scene => _catalog.IsModuleActive(scene.Module, modules)).Select(scene => ExtensibilityCopies.Scene(scene) with { SourceScenarioStateId = snapshot.Id }).ToList();
        foreach ((ModuleId module, IScenarioDefinitionExtension provider) in _extensions.ScenarioDefinitionExtensions.Where(value => _catalog.IsModuleActive(value.Module, modules)))
        {
            var context = new SceneDefinitionContext { Scenario = ScenarioExtensibilityAdapter.ToView(snapshot), RandomSeed = randomSeed, Parameters = GetModuleParameters(module) };
            IReadOnlyList<SceneDefinition> provided = await provider.EvaluateAsync(context, cancellationToken);
            foreach (SceneDefinition definition in provided)
            {
                ValidateProviderDefinition(module, provider, definition, snapshot.Id);
                definitions.Add(ExtensibilityCopies.Scene(definition) with { SourceScenarioStateId = snapshot.Id });
            }
        }
        SemanticKey? duplicate = definitions.GroupBy(value => value.Id).Where(group => group.Count() > 1).Select(group => (SemanticKey?)group.Key).FirstOrDefault();
        if (duplicate.HasValue)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidModule, TaviErrorCategory.Protocol, nameof(GetSceneDefinitionsAsync), $"多个 Provider 返回了重复 SceneDefinition {duplicate.Value}。");
        return definitions.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    internal ScenarioCommitResult CreateScene(SceneDefinition definition, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ScenarioSnapshot snapshot = ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(CreateScene));
            return scenario.CreateSnapshot();
        });
        ValidateSceneDefinitionAvailability(definition, snapshot, expectedStateId, nameof(CreateScene));
        Guid sceneId = Guid.NewGuid();
        return Commit(new ScenarioChangeSet([new AddSceneOperation(sceneId, new SceneDefinitionType(definition.Id.Value), definition.Module.Value, definition.ModuleVersion.Value, expectedStateId, definition.Name, definition.Description, ScenarioExtensibilityAdapter.ToDomain(definition.SettlementCapabilities), ScenarioExtensibilityAdapter.ToDomainSlots(definition))]), expectedStateId);
    }

    /// <inheritdoc />
    internal ScenarioCommitResult SetSceneBinding(Guid sceneId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(elementIds);
        ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(SetSceneBinding));
            Scene scene = scenario.GetScene(sceneId);
            EnsureSceneState(scene, SceneState.Binding, nameof(SetSceneBinding));
            SceneDefinition definition = ScenarioExtensibilityAdapter.ToDefinition(scene);
            SceneSlotDefinition slot = definition.Slots.SingleOrDefault(value => value.Id == slotId) ?? throw Semantic(nameof(SetSceneBinding), $"Scene {sceneId} 不包含槽位 {slotId}。");
            ValidateSlotBinding(slot, elementIds, scenario.CreateSnapshot(), nameof(SetSceneBinding));
            return true;
        });
        return Commit(new ScenarioChangeSet([new SetSceneSlotBindingOperation(sceneId, new SceneSlotBinding(slotId, elementIds))]), expectedStateId);
    }

    /// <inheritdoc />
    internal ScenarioCommitResult ClearSceneBinding(Guid sceneId, string slotId, Guid expectedStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(ClearSceneBinding));
            EnsureSceneState(scenario.GetScene(sceneId), SceneState.Binding, nameof(ClearSceneBinding));
            return true;
        });
        return Commit(new ScenarioChangeSet([new ClearSceneSlotBindingOperation(sceneId, slotId)]), expectedStateId);
    }

    /// <inheritdoc />
    internal ScenarioCommitResult BeginSceneProcessing(Guid sceneId, Guid expectedStateId)
    {
        ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(BeginSceneProcessing));
            Scene scene = scenario.GetScene(sceneId);
            EnsureSceneState(scene, SceneState.Binding, nameof(BeginSceneProcessing));
            if (!scene.DefinitionFrozen)
                throw InvalidState(nameof(BeginSceneProcessing), $"Scene {scene.Id} 来自未保存完整槽位定义的旧存档；请删除并从当前 SceneDefinition 重新创建。");
            ValidateBindings(ScenarioExtensibilityAdapter.ToDefinition(scene), scene.GetBindings().ToDictionary(binding => binding.SlotId, binding => binding.ElementIds), scenario.CreateSnapshot(), nameof(BeginSceneProcessing));
            return true;
        });
        return Commit(new ScenarioChangeSet([new UpdateSceneStateOperation(sceneId, SceneState.Processing)]), expectedStateId);
    }

    /// <inheritdoc />
    internal SceneContextView GetProcessingContext(Guid sceneId) => ExecuteQuery(scenario =>
    {
        Scene scene = scenario.GetScene(sceneId);
        EnsureSceneState(scene, SceneState.Processing, nameof(GetProcessingContext));
        return ScenarioExtensibilityAdapter.ToSceneContext(scenario.CreateSnapshot(), scene);
    });

    /// <inheritdoc />
    internal async Task<ScenarioCommitResult> SettleSceneByRulesAsync(Guid sceneId, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default)
    {
        (ScenarioSnapshot Snapshot, Scene Scene) captured = ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(SettleSceneByRulesAsync));
            Scene scene = scenario.GetScene(sceneId);
            EnsureSceneCanUse(scene, SceneSettlementOptions.Rules, SceneState.Processing, nameof(SettleSceneByRulesAsync));
            return (scenario.CreateSnapshot(), scene);
        });
        SceneDefinition definition = ScenarioExtensibilityAdapter.ToDefinition(captured.Scene);
        IScenarioSettlementExtension settler = _extensions.ScenarioSettlementExtensions.Where(value => value.Module == definition.Module).Select(value => value.Extension).SingleOrDefault(value => value.Definitions.Contains(definition.Id)) ?? throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.CapabilityUnavailable, TaviErrorCategory.Configuration, nameof(SettleSceneByRulesAsync), $"SceneDefinition {definition.Id} 没有注册规则结算器。");
        var context = new SceneSettlementContext { Context = ScenarioExtensibilityAdapter.ToSceneContext(captured.Snapshot, captured.Scene), Definition = definition, RandomSeed = randomSeed, Parameters = GetModuleParameters(definition.Module) };
        SceneSettlementProposal proposal = await settler.SettleAsync(context, cancellationToken);
        return SettleScene(sceneId, proposal, expectedStateId);
    }

    /// <inheritdoc />
    internal ScenarioCommitResult SettleScene(Guid sceneId, SceneSettlementProposal proposal, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.ExpectedScenarioStateId != expectedStateId)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(SettleScene), "Scene 结算提案未基于请求的 Scenario StateId。");
        (ScenarioSnapshot Snapshot, Scene Scene) captured = ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(SettleScene));
            Scene scene = scenario.GetScene(sceneId);
            EnsureSceneState(scene, SceneState.Processing, nameof(SettleScene));
            return (scenario.CreateSnapshot(), scene);
        });
        ScenarioChangeSet proposed = ScenarioExtensibilityAdapter.ToLocalChangeSet(proposal, captured.Snapshot, captured.Scene);
        return Commit(new ScenarioChangeSet(new ScenarioOperation[] { new UpdateSceneStateOperation(sceneId, SceneState.Settled) }.Concat(proposed.Operations)), expectedStateId);
    }

    /// <inheritdoc />
    internal ScenarioCommitResult RemoveScene(Guid sceneId, Guid expectedStateId) => Commit(new ScenarioChangeSet([new RemoveSceneOperation(sceneId)]), expectedStateId);

    /// <inheritdoc />
    internal ScenarioCommitResult ClearSettledScenes(Guid expectedStateId) => Commit(new ScenarioChangeSet([new ClearSettledScenesOperation()]), expectedStateId);

    /// <inheritdoc />
    internal ScenarioCommitResult Undo(Guid expectedStateId)
    {
        ScenarioCommitResult result = _workspace.ExecuteExclusive(() =>
        {
            if (!_workspace.CanUndo)
                throw InvalidState(nameof(Undo), "没有可撤销的 Scenario 操作。");
            return ExecuteWorkspace(() => _workspace.Undo(expectedStateId), nameof(Undo));
        });
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    internal ScenarioCommitResult Redo(Guid expectedStateId)
    {
        ScenarioCommitResult result = _workspace.ExecuteExclusive(() =>
        {
            if (!_workspace.CanRedo)
                throw InvalidState(nameof(Redo), "没有可重做的 Scenario 操作。");
            return ExecuteWorkspace(() => _workspace.Redo(expectedStateId), nameof(Redo));
        });
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    internal Task SaveAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingAutoSave();
        return SaveCoreAsync(true, cancellationToken);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingAutoSave();
        return SaveCoreAsync(false, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        try
        {
            await CancelAndDrainAutoSaveAsync();
            if (_workspace.IsInitialized && Health == ScenarioSessionHealth.Healthy)
                await SaveCoreAsync(false, CancellationToken.None);
        }
        finally
        {
            _disposed = true;
            _saveGate.Dispose();
        }
    }

    internal TResult ExecuteQuery<TResult>(Func<RuntimeScenario, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();
        return _workspace.Read(query);
    }

    private ScenarioCommitResult ExecuteWorkspace(Func<VersionedCommitResult<AppliedScenarioChangeSet>> action, string operation)
    {
        try
        {
            VersionedCommitResult<AppliedScenarioChangeSet> result = action();
            return result.Changed
                ? new ScenarioCommitResult(result.CommitId, result.PreviousStateId, result.StateId, result.History)
                : ScenarioCommitResult.Unchanged(result.StateId);
        }
        catch (OptimisticConcurrencyConflictException exception)
        {
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, $"Scenario 状态冲突：期望 {exception.ExpectedStateId}，实际 {exception.ActualStateId}。");
        }
        catch (AtomicStateRecoveryException exception) when (exception.InnerException is ScenarioException cause)
        {
            throw cause;
        }
    }

    private async Task SaveCoreAsync(bool force, CancellationToken cancellationToken)
    {
        EnsureUsable();
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            (ScenarioSnapshot Snapshot, Guid StateId, bool Saved) state = ExecuteQuery(scenario => (scenario.CreateSnapshot(), scenario.StateId, scenario.StateId == _savedStateId));
            if (!force && state.Saved)
                return;
            await _store.SaveAsync(_slot, state.Snapshot, cancellationToken);
            _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = state.StateId;
                return true;
            });
            LastAutoSaveException = null;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private ScenarioSnapshot CreateInitialSnapshot() => _initialSnapshot is null ? new ScenarioSnapshot { SourceWorldStateId = _sourceWorldStateId, Modules = _catalog.Modules.Select(module => new ScenarioModuleReference(module.Id.Value, module.Version.Value, GetModuleParameters(module.Id))).ToList() } : RuntimeScenario.Create(_initialSnapshot).CreateSnapshot();

    private IReadOnlyDictionary<string, string> GetModuleParameters(ModuleId module) => _moduleParameters.GetValueOrDefault(module) ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private void EnsureFrozenParametersMatch(ScenarioSnapshot snapshot)
    {
        for (int index = 0; index < snapshot.Modules.Count; index++)
        {
            ScenarioModuleReference reference = snapshot.Modules[index];
            var id = new ModuleId(reference.Id);
            IReadOnlyDictionary<string, string> expected = GetModuleParameters(id);
            if (reference.Parameters.Count == 0 && expected.Count > 0)
            {
                snapshot.Modules[index] = new ScenarioModuleReference(reference.Id, reference.Version, expected);
                continue;
            }
            if (reference.Parameters.Count != expected.Count || reference.Parameters.Any(pair => expected.GetValueOrDefault(pair.Key) != pair.Value))
                throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidModule, TaviErrorCategory.Configuration, nameof(InitializeAsync), $"Scenario 存档中的 Module {id} 参数与当前冻结 Extension 配置不一致；请使用匹配配置重新加载。");
        }
    }

    private static IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> CopyParameters(IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> source) => new ReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>>(source.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(pair.Value, StringComparer.Ordinal))));

    private void ValidateBindings(SceneDefinition definition, IReadOnlyDictionary<string, IReadOnlyList<Guid>> bindings, ScenarioSnapshot snapshot, string operation)
    {
        string? unknown = bindings.Keys.FirstOrDefault(id => definition.Slots.All(slot => slot.Id != id));
        if (unknown is not null)
            throw Semantic(operation, $"SceneDefinition {definition.Id} 不包含槽位 {unknown}。");
        foreach (SceneSlotDefinition slot in definition.Slots)
        {
            IReadOnlyList<Guid> values = bindings.GetValueOrDefault(slot.Id, []);
            ValidateSlotBinding(slot, values, snapshot, operation);
        }
    }

    private void ValidateSlotBinding(SceneSlotDefinition slot, IReadOnlyList<Guid> values, ScenarioSnapshot snapshot, string operation)
    {
        if (values.Count > (slot.Maximum ?? int.MaxValue) || values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Count)
            throw Semantic(operation, $"槽位 {slot.Id} 的绑定数量或标识无效。");
        foreach (Guid id in values)
        {
            if (!snapshot.Elements.TryGetValue(id, out Element? element) || !_validator.ElementMatchesSlot(snapshot, element, slot.Requirement))
                throw Semantic(operation, $"Element {id} 不满足槽位 {slot.Id}。");
        }
        if (operation == nameof(BeginSceneProcessing) && values.Count < slot.Minimum)
            throw Semantic(operation, $"槽位 {slot.Id} 至少需要 {slot.Minimum} 个 Element。");
    }

    private void ValidateSceneDefinitionAvailability(SceneDefinition definition, ScenarioSnapshot snapshot, Guid expectedStateId, string operation)
    {
        Dictionary<string, string> modules = snapshot.Modules.ToDictionary(value => value.Id, value => value.Version, StringComparer.Ordinal);
        if (!_catalog.IsModuleActive(definition.Module, modules) || !_catalog.Modules.Any(module => module.Id == definition.Module && module.Version == definition.ModuleVersion))
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.CapabilityUnavailable, TaviErrorCategory.Configuration, operation, $"SceneDefinition {definition.Id} 所需 Module 未激活。");
        if (definition.Id.Namespace != definition.Module)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidModule, TaviErrorCategory.Protocol, operation, $"SceneDefinition {definition.Id} 不属于 Module {definition.Module}。");
        if (definition.SourceScenarioStateId.HasValue && definition.SourceScenarioStateId.Value != expectedStateId)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, $"SceneDefinition {definition.Id} 已经过期。");
    }

    private static void ValidateProviderDefinition(ModuleId module, IScenarioDefinitionExtension provider, SceneDefinition definition, Guid stateId)
    {
        if (definition is null || definition.Module != module || definition.Id.Namespace != module || definition.SourceScenarioStateId.HasValue && definition.SourceScenarioStateId.Value != stateId || definition.SettlementCapabilities == SceneSettlementCapabilities.None)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidModule, TaviErrorCategory.Protocol, nameof(GetSceneDefinitionsAsync), $"Provider {provider.GetType().FullName} 返回了无效 SceneDefinition。");
    }

    private static void EnsureSceneCanUse(Scene scene, SceneSettlementOptions option, SceneState requiredState, string operation)
    {
        EnsureSceneState(scene, requiredState, operation);
        if (!scene.SettlementOptions.HasFlag(option))
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.CapabilityUnavailable, TaviErrorCategory.InvalidState, operation, $"Scene {scene.Id} 未声明请求的结算能力。");
    }

    private static void EnsureSceneState(Scene scene, SceneState requiredState, string operation)
    {
        if (scene.State != requiredState)
            throw InvalidState(operation, $"Scene {scene.Id} 当前状态 {scene.State}，请求需要 {requiredState}。");
    }

    private static void EnsureExpectedState(RuntimeScenario scenario, Guid expectedStateId, string operation)
    {
        if (expectedStateId == Guid.Empty || scenario.StateId != expectedStateId)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, $"Scenario 状态冲突：期望 {expectedStateId}，实际 {scenario.StateId}。");
    }

    private void Publish(ScenarioCommitResult result)
    {
        if (result.Changed)
        {
            ScheduleAutoSave();
            Changed?.Invoke(this, new ScenarioSessionChangedEventArgs(result));
        }
    }

    private void ScheduleAutoSave()
    {
        if (Health != ScenarioSessionHealth.Healthy || _disposed)
            return;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource?.Dispose();
            var source = new CancellationTokenSource();
            _debounceSource = source;
            Task task = RunAutoSaveAsync(source);
            _autoSaveTasks.Add(task);
            _ = task.ContinueWith(_ =>
            {
                lock (_debounceSync)
                    _autoSaveTasks.Remove(task);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
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

    private async Task CancelAndDrainAutoSaveAsync()
    {
        Task[] tasks;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource = null;
            tasks = _autoSaveTasks.ToArray();
        }
        if (tasks.Length > 0)
            await Task.WhenAll(tasks);
    }

    private TResult ExecuteLocked<TResult>(Func<TResult> action)
    {
        ThrowIfDisposed();
        return _workspace.ExecuteExclusive(action);
    }

    private void EnsureUsable()
    {
        ThrowIfDisposed();
        _workspace.Read(_ => true);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static ScenarioApplicationException InvalidState(string operation, string message) => new(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, operation, message);

    private static ScenarioApplicationException Semantic(string operation, string message) => new(ScenarioApplicationErrorCodes.SemanticViolation, TaviErrorCategory.Validation, operation, message);

}
