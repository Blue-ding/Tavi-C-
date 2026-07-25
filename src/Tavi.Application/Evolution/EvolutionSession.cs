using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Application.Evolution;

/// <summary>独占当前 Scenario，并统一控制 Module 调度、Scene 结算、原子修改、历史和持久化。</summary>
public sealed class EvolutionSession : IEvolutionService
{
    private readonly IScenarioStore _store;
    private readonly EvolutionModuleCatalog _catalog;
    private readonly ScenarioSemanticValidator _validator;
    private readonly Guid _sourceWorldStateId;
    private readonly string _slot;
    private readonly TimeSpan _autoSaveDelay;
    private readonly object _scenarioSync = new();
    private readonly object _debounceSync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Stack<AppliedScenarioChangeSet> _undoHistory = new();
    private readonly Stack<AppliedScenarioChangeSet> _redoHistory = new();
    private readonly List<Task> _autoSaveTasks = [];
    private CancellationTokenSource? _debounceSource;
    private RuntimeScenario? _current;
    private Guid _savedStateId;
    private bool _disposed;

    /// <summary>创建持有独立 Scenario 的 EvolutionSession。</summary>
    public EvolutionSession(IScenarioStore store, EvolutionModuleCatalog catalog, Guid sourceWorldStateId, string slot = "default", TimeSpan? autoSaveDelay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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
        Queries = new ScenarioQueries(this);
    }

    /// <inheritdoc />
    public event EventHandler<EvolutionSessionChangedEventArgs>? Changed;

    /// <inheritdoc />
    public ScenarioQueries Queries { get; }

    /// <inheritdoc />
    public EvolutionSessionHealth Health { get; private set; } = EvolutionSessionHealth.Healthy;

    /// <inheritdoc />
    public Guid StateId => ExecuteQuery(scenario => scenario.StateId);

    /// <inheritdoc />
    public bool IsDirty => ExecuteQuery(scenario => scenario.StateId != _savedStateId);

    /// <inheritdoc />
    public bool CanUndo => ExecuteLocked(() => _undoHistory.Count > 0);

    /// <inheritdoc />
    public bool CanRedo => ExecuteLocked(() => _redoHistory.Count > 0);

    /// <inheritdoc />
    public Exception? LastAutoSaveException { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_scenarioSync)
        {
            if (_current is not null)
                throw InvalidState(nameof(InitializeAsync), "EvolutionSession 已经初始化。");
        }
        ScenarioSnapshot? loaded = await _store.LoadAsync(_slot, cancellationToken);
        ScenarioSnapshot snapshot = loaded ?? CreateInitialSnapshot();
        RuntimeScenario scenario = RuntimeScenario.Create(snapshot);
        _validator.EnsureValid(scenario.CreateSnapshot(), nameof(InitializeAsync));
        lock (_scenarioSync)
        {
            if (_current is not null)
                throw InvalidState(nameof(InitializeAsync), "EvolutionSession 已经初始化。");
            _current = scenario;
            _savedStateId = loaded is null ? Guid.Empty : scenario.StateId;
            _undoHistory.Clear();
            _redoHistory.Clear();
            Health = EvolutionSessionHealth.Healthy;
        }
    }

    /// <inheritdoc />
    public ScenarioCommitResult Apply(ScenarioChangeSet changeSet, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ScenarioCommitResult result = ApplyCore(changeSet, expectedStateId, HistoryAction.Record);
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SceneDefinition>> GetSceneDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default)
    {
        ScenarioSnapshot snapshot = ExecuteQuery(scenario => scenario.CreateSnapshot());
        Dictionary<string, string> modules = snapshot.Modules.ToDictionary(value => value.Id, value => value.Version, StringComparer.Ordinal);
        var definitions = _catalog.StaticScenes.Where(scene => _catalog.IsModuleActive(scene.Module, modules)).Select(scene => ExtensibilityCopies.Scene(scene) with { SourceScenarioStateId = snapshot.Id }).ToList();
        var context = new SceneDefinitionContext { Scenario = ScenarioExtensibilityAdapter.ToView(snapshot), RandomSeed = randomSeed };
        foreach (ISceneDefinitionProvider provider in _catalog.SceneProviders.Where(provider => _catalog.IsModuleActive(provider.Module, modules)))
        {
            IReadOnlyList<SceneDefinition> provided = await provider.EvaluateAsync(context, cancellationToken);
            foreach (SceneDefinition definition in provided)
            {
                ValidateProviderDefinition(provider, definition, snapshot.Id);
                definitions.Add(ExtensibilityCopies.Scene(definition) with { SourceScenarioStateId = snapshot.Id });
            }
        }
        SemanticKey? duplicate = definitions.GroupBy(value => value.Id).Where(group => group.Count() > 1).Select(group => (SemanticKey?)group.Key).FirstOrDefault();
        if (duplicate.HasValue)
            throw new EvolutionException(EvolutionErrorCodes.InvalidModule, TaviErrorCategory.Protocol, nameof(GetSceneDefinitionsAsync), $"多个 Provider 返回了重复 SceneDefinition {duplicate.Value}。");
        return definitions.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public ScenarioCommitResult CreateScene(SceneDefinition definition, IReadOnlyDictionary<string, IReadOnlyList<Guid>> bindings, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bindings);
        ScenarioSnapshot snapshot = ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(CreateScene));
            return scenario.CreateSnapshot();
        });
        ValidateSceneDefinitionAvailability(definition, snapshot, expectedStateId, nameof(CreateScene));
        ValidateBindings(definition, bindings, snapshot);
        Guid sceneId = Guid.NewGuid();
        var operations = new List<ScenarioOperation>
        {
            new AddSceneOperation(sceneId, new SceneDefinitionType(definition.Id.Value), definition.Module.Value, definition.ModuleVersion.Value, expectedStateId, definition.Name, definition.Description, ScenarioExtensibilityAdapter.ToDomain(definition.SettlementCapabilities))
        };
        operations.AddRange(definition.Slots.Where(slot => bindings.TryGetValue(slot.Id, out IReadOnlyList<Guid>? values) && values.Count > 0).Select(slot => new SetSceneSlotBindingOperation(sceneId, new SceneSlotBinding(slot.Id, bindings[slot.Id]))));
        operations.Add(new UpdateSceneStateOperation(sceneId, SceneState.Ready));
        return Apply(new ScenarioChangeSet(operations), expectedStateId);
    }

    /// <inheritdoc />
    public async Task<ScenarioCommitResult> SettleSceneByRulesAsync(Guid sceneId, SceneDefinition definition, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        (ScenarioSnapshot Snapshot, Scene Scene) captured = ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(SettleSceneByRulesAsync));
            Scene scene = scenario.GetScene(sceneId);
            EnsureSceneCanUse(scene, SceneSettlementOptions.Rules, SceneState.Ready, nameof(SettleSceneByRulesAsync));
            return (scenario.CreateSnapshot(), scene);
        });
        ValidateSceneDefinitionAvailability(definition, captured.Snapshot, expectedStateId, nameof(SettleSceneByRulesAsync));
        ISceneRuleSettler settler = _catalog.RuleSettlers.SingleOrDefault(value => value.Module == definition.Module && value.Definitions.Contains(definition.Id)) ?? throw new EvolutionException(EvolutionErrorCodes.CapabilityUnavailable, TaviErrorCategory.Configuration, nameof(SettleSceneByRulesAsync), $"SceneDefinition {definition.Id} 没有注册规则结算器。");
        var context = new RuleSettlementContext { Scenario = ScenarioExtensibilityAdapter.ToView(captured.Snapshot), Scene = ScenarioExtensibilityAdapter.ToView(captured.Scene), Definition = definition, RandomSeed = randomSeed };
        ScenarioChangeProposal proposal = await settler.SettleAsync(context, cancellationToken);
        if (proposal.ExpectedScenarioStateId != expectedStateId)
            throw new EvolutionException(EvolutionErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(SettleSceneByRulesAsync), "规则结算提案未基于请求的 Scenario StateId。");
        ScenarioChangeSet proposed = ScenarioExtensibilityAdapter.ToChangeSet(proposal);
        return Apply(new ScenarioChangeSet(proposed.Operations.Append(new UpdateSceneStateOperation(sceneId, SceneState.Settled))), expectedStateId);
    }

    /// <inheritdoc />
    public ScenarioCommitResult BeginSceneWriting(Guid sceneId, Guid expectedStateId)
    {
        ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(BeginSceneWriting));
            EnsureSceneCanUse(scenario.GetScene(sceneId), SceneSettlementOptions.Writing, SceneState.Ready, nameof(BeginSceneWriting));
            return true;
        });
        return Apply(new ScenarioChangeSet([new UpdateSceneStateOperation(sceneId, SceneState.AwaitingWriting)]), expectedStateId);
    }

    /// <inheritdoc />
    public ScenarioCommitResult CommitWrittenSceneOutcome(Guid sceneId, ScenarioChangeProposal outcome, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.ExpectedScenarioStateId != expectedStateId)
            throw new EvolutionException(EvolutionErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(CommitWrittenSceneOutcome), "Writing 结果未基于请求的 Scenario StateId。");
        ExecuteQuery(scenario =>
        {
            EnsureExpectedState(scenario, expectedStateId, nameof(CommitWrittenSceneOutcome));
            Scene scene = scenario.GetScene(sceneId);
            if (scene.State != SceneState.AwaitingWriting)
                throw InvalidState(nameof(CommitWrittenSceneOutcome), $"Scene {sceneId} 当前不是 AwaitingWriting。");
            return true;
        });
        ScenarioChangeSet proposed = ScenarioExtensibilityAdapter.ToChangeSet(outcome);
        return Apply(new ScenarioChangeSet(proposed.Operations.Append(new UpdateSceneStateOperation(sceneId, SceneState.Settled))), expectedStateId);
    }

    /// <inheritdoc />
    public ScenarioCommitResult Undo(Guid expectedStateId)
    {
        ScenarioCommitResult result = ApplyCore(null, expectedStateId, HistoryAction.Undo);
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    public ScenarioCommitResult Redo(Guid expectedStateId)
    {
        ScenarioCommitResult result = ApplyCore(null, expectedStateId, HistoryAction.Redo);
        Publish(result);
        return result;
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
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
            if (_current is not null && Health == EvolutionSessionHealth.Healthy)
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
        return ExecuteLocked(() => query(RequireCurrent()));
    }

    private ScenarioCommitResult ApplyCore(ScenarioChangeSet? requested, Guid expectedStateId, HistoryAction action)
    {
        lock (_scenarioSync)
        {
            EnsureUsableLocked();
            RuntimeScenario scenario = RequireCurrent();
            EnsureExpectedState(scenario, expectedStateId, nameof(Apply));
            AppliedScenarioChangeSet? history = action switch
            {
                HistoryAction.Undo => _undoHistory.TryPeek(out AppliedScenarioChangeSet? undo) ? undo : throw InvalidState(nameof(Undo), "没有可撤销的 Scenario 操作。"),
                HistoryAction.Redo => _redoHistory.TryPeek(out AppliedScenarioChangeSet? redo) ? redo : throw InvalidState(nameof(Redo), "没有可重做的 Scenario 操作。"),
                _ => null
            };
            ScenarioChangeSet changeSet = action switch
            {
                HistoryAction.Undo => history!.Inverse,
                HistoryAction.Redo => history!.Forward,
                _ => requested!
            };
            RuntimeScenario projection = RuntimeScenario.Create(scenario.CreateSnapshot());
            _ = projection.Apply(changeSet);
            _validator.EnsureValid(projection.CreateSnapshot(), nameof(Apply));
            ScenarioApplyResult applied;
            try
            {
                applied = scenario.Apply(changeSet);
            }
            catch (ScenarioException exception) when (exception.ErrorCode == ScenarioErrorCodes.RollbackFailed)
            {
                Health = EvolutionSessionHealth.Faulted;
                throw;
            }
            if (!applied.Changed)
                return ScenarioCommitResult.Unchanged(scenario.StateId);
            switch (action)
            {
                case HistoryAction.Record:
                    _undoHistory.Push(applied.ChangeSet!);
                    _redoHistory.Clear();
                    break;
                case HistoryAction.Undo:
                    _undoHistory.Pop();
                    _redoHistory.Push(history!);
                    break;
                case HistoryAction.Redo:
                    _redoHistory.Pop();
                    _undoHistory.Push(history!);
                    break;
            }
            return new ScenarioCommitResult(Guid.NewGuid(), applied.PreviousStateId, applied.StateId, applied.ChangeSet);
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
            lock (_scenarioSync)
                _savedStateId = state.StateId;
            LastAutoSaveException = null;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private ScenarioSnapshot CreateInitialSnapshot() => new()
    {
        SourceWorldStateId = _sourceWorldStateId,
        Modules = _catalog.Modules.Select(module => new ScenarioModuleReference(module.Id.Value, module.Version.Value)).ToList()
    };

    private void ValidateBindings(SceneDefinition definition, IReadOnlyDictionary<string, IReadOnlyList<Guid>> bindings, ScenarioSnapshot snapshot)
    {
        string? unknown = bindings.Keys.FirstOrDefault(id => definition.Slots.All(slot => slot.Id != id));
        if (unknown is not null)
            throw new EvolutionException(EvolutionErrorCodes.SemanticViolation, TaviErrorCategory.Validation, nameof(CreateScene), $"SceneDefinition {definition.Id} 不包含槽位 {unknown}。");
        foreach (SceneSlotDefinition slot in definition.Slots)
        {
            IReadOnlyList<Guid> values = bindings.GetValueOrDefault(slot.Id, []);
            if (values.Count < slot.Minimum || slot.Maximum.HasValue && values.Count > slot.Maximum.Value || values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Count)
                throw new EvolutionException(EvolutionErrorCodes.SemanticViolation, TaviErrorCategory.Validation, nameof(CreateScene), $"槽位 {slot.Id} 的绑定数量或标识无效。");
            foreach (Guid id in values)
            {
                if (!snapshot.Elements.TryGetValue(id, out Element? element) || !_validator.ElementMatchesSlot(snapshot, element, slot.Requirement))
                    throw new EvolutionException(EvolutionErrorCodes.SemanticViolation, TaviErrorCategory.Validation, nameof(CreateScene), $"Element {id} 不满足槽位 {slot.Id}。");
            }
        }
    }

    private void ValidateSceneDefinitionAvailability(SceneDefinition definition, ScenarioSnapshot snapshot, Guid expectedStateId, string operation)
    {
        Dictionary<string, string> modules = snapshot.Modules.ToDictionary(value => value.Id, value => value.Version, StringComparer.Ordinal);
        if (!_catalog.IsModuleActive(definition.Module, modules) || !_catalog.Modules.Any(module => module.Id == definition.Module && module.Version == definition.ModuleVersion))
            throw new EvolutionException(EvolutionErrorCodes.CapabilityUnavailable, TaviErrorCategory.Configuration, operation, $"SceneDefinition {definition.Id} 所需 Module 未激活。");
        if (definition.Id.Namespace != definition.Module)
            throw new EvolutionException(EvolutionErrorCodes.InvalidModule, TaviErrorCategory.Protocol, operation, $"SceneDefinition {definition.Id} 不属于 Module {definition.Module}。");
        if (definition.SourceScenarioStateId.HasValue && definition.SourceScenarioStateId.Value != expectedStateId)
            throw new EvolutionException(EvolutionErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, $"SceneDefinition {definition.Id} 已经过期。");
    }

    private static void ValidateProviderDefinition(ISceneDefinitionProvider provider, SceneDefinition definition, Guid stateId)
    {
        if (definition is null || definition.Module != provider.Module || definition.Id.Namespace != provider.Module || definition.SourceScenarioStateId.HasValue && definition.SourceScenarioStateId.Value != stateId || definition.SettlementCapabilities == SceneSettlementCapabilities.None)
            throw new EvolutionException(EvolutionErrorCodes.InvalidModule, TaviErrorCategory.Protocol, nameof(GetSceneDefinitionsAsync), $"Provider {provider.GetType().FullName} 返回了无效 SceneDefinition。");
    }

    private static void EnsureSceneCanUse(Scene scene, SceneSettlementOptions option, SceneState requiredState, string operation)
    {
        if (scene.State != requiredState)
            throw InvalidState(operation, $"Scene {scene.Id} 当前状态 {scene.State} 不能执行该操作。");
        if (!scene.SettlementOptions.HasFlag(option))
            throw new EvolutionException(EvolutionErrorCodes.CapabilityUnavailable, TaviErrorCategory.InvalidState, operation, $"Scene {scene.Id} 未声明请求的结算能力。");
    }

    private static void EnsureExpectedState(RuntimeScenario scenario, Guid expectedStateId, string operation)
    {
        if (expectedStateId == Guid.Empty || scenario.StateId != expectedStateId)
            throw new EvolutionException(EvolutionErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, $"Scenario 状态冲突：期望 {expectedStateId}，实际 {scenario.StateId}。");
    }

    private void Publish(ScenarioCommitResult result)
    {
        if (result.Changed)
        {
            ScheduleAutoSave();
            Changed?.Invoke(this, new EvolutionSessionChangedEventArgs(result));
        }
    }

    private void ScheduleAutoSave()
    {
        if (Health != EvolutionSessionHealth.Healthy || _disposed)
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
        lock (_scenarioSync)
        {
            EnsureUsableLocked();
            return action();
        }
    }

    private void EnsureUsable()
    {
        lock (_scenarioSync)
            EnsureUsableLocked();
    }

    private void EnsureUsableLocked()
    {
        ThrowIfDisposed();
        if (Health == EvolutionSessionHealth.Faulted)
            throw InvalidState(nameof(EvolutionSession), "EvolutionSession 已进入 Faulted 状态。");
        _ = RequireCurrent();
    }

    private RuntimeScenario RequireCurrent() => _current ?? throw InvalidState(nameof(EvolutionSession), "EvolutionSession 尚未初始化。");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static EvolutionException InvalidState(string operation, string message) => new(EvolutionErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, operation, message);

    private enum HistoryAction
    {
        Record,
        Undo,
        Redo
    }
}
