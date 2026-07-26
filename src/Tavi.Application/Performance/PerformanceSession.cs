using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.LanguageModel;
using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using Tavi.Utilities.Concurrency;
using RuntimePerformance = Tavi.Domain.Performance.Performance;

namespace Tavi.Application.Performance;

/// <summary>持有唯一活动 Performance，并统一控制恢复、Module 演绎、历史和持久化。</summary>
public sealed class PerformanceSession : IPerformanceWorkspace, IPerformanceSessionLifecycle
{
    private readonly IPerformanceStore _store;
    private readonly IBeatPublisher _beatPublisher;
    private readonly FrozenModuleRuntime _extensions;
    private readonly BeatNarrationRenderer _narration;
    private readonly SceneContextView? _initialScene;
    private readonly long _initialRandomSeed;
    private readonly TimeSpan _autoSaveDelay;
    private readonly VersionedWorkspace<RuntimePerformance, PerformanceChangeSet, AppliedPerformanceChangeSet> _workspace = new(new PerformanceConcurrencyModel());
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _debounceSync = new();
    private readonly List<Task> _autoSaveTasks = [];
    private CancellationTokenSource? _debounceSource;
    private IPerformanceExtension? _extension;
    private SceneContextView? _sourceScene;
    private Guid _savedStateId;
    private bool _disposed;

    /// <summary>创建用于恢复 Store 中唯一活动 Performance 的 Session。</summary>
    public PerformanceSession(
        IPerformanceStore store,
        IBeatPublisher beatPublisher,
        FrozenModuleRuntime extensions,
        TimeSpan? autoSaveDelay = null,
        ILanguageModelService? languageModels = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _beatPublisher = beatPublisher ?? throw new ArgumentNullException(nameof(beatPublisher));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _narration = new BeatNarrationRenderer(_extensions, languageModels);
        _autoSaveDelay = autoSaveDelay ?? TimeSpan.FromSeconds(1);
        if (_autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay));
        Queries = new PerformanceQueries(this);
        Commands = new PerformanceCommands(this);
    }

    /// <summary>创建会在 Store 为空时从冻结 Processing Scene 展开新 Performance 的 Session。</summary>
    public PerformanceSession(
        IPerformanceStore store,
        SceneContextView sourceScene,
        long randomSeed,
        IBeatPublisher beatPublisher,
        FrozenModuleRuntime extensions,
        TimeSpan? autoSaveDelay = null,
        ILanguageModelService? languageModels = null)
        : this(store, beatPublisher, extensions, autoSaveDelay, languageModels)
    {
        _initialScene = sourceScene ?? throw new ArgumentNullException(nameof(sourceScene));
        _initialRandomSeed = randomSeed;
        if (!string.Equals(sourceScene.Scene.State, "Processing", StringComparison.Ordinal))
            throw new ArgumentException("Performance 只能接收 Processing Scene。", nameof(sourceScene));
    }

    public event EventHandler<PerformanceSessionChangedEventArgs>? Changed;

    public Guid Id => Read(value => value.Id);
    public Guid StateId => Read(value => value.StateId);
    public PerformanceStatus Status => Read(value => value.Status);
    public PerformanceSessionHealth Health => _workspace.Health == VersionedWorkspaceHealth.Healthy ? PerformanceSessionHealth.Healthy : PerformanceSessionHealth.Faulted;
    public bool IsDirty => Read(value => value.StateId != _savedStateId);
    public bool CanUndo => _workspace.CanUndo;
    public bool CanRedo => _workspace.CanRedo;
    public Exception? LastAutoSaveException { get; private set; }
    public PerformanceQueries Queries { get; }
    public PerformanceCommands Commands { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_workspace.IsInitialized)
            throw new InvalidOperationException("PerformanceSession 已经初始化。");

        PerformanceSnapshot? loaded = await _store.LoadActiveAsync(cancellationToken);
        PerformanceSnapshot snapshot;
        if (loaded is not null)
        {
            if (_initialScene is not null)
                throw new InvalidOperationException($"已有活动 Performance {loaded.PerformanceId}，不能开始新的 Performance。");
            snapshot = loaded;
        }
        else
        {
            SceneContextView scene = _initialScene ?? throw new InvalidOperationException("Store 中不存在可恢复的活动 Performance。");
            IPerformanceExtension extension = ResolveExtension(scene.Scene.Module, scene.Scene.ModuleVersion);
            PerformanceExpansionProposal expansion = await extension.ExpandAsync(new PerformanceExpansionContext
            {
                Scene = scene,
                RandomSeed = _initialRandomSeed,
                Parameters = _extensions.GetParameters(scene.Scene.Module)
            }, cancellationToken);
            if (expansion.ExpectedScenarioStateId != scene.ScenarioStateId)
                throw new InvalidOperationException("Performance 展开提案未基于冻结 Scene 的 Scenario StateId。");
            RuntimePerformance created = RuntimePerformance.Create(PerformanceExtensibilityAdapter.CreateSeed(scene, _extensions));
            _ = created.Apply(PerformanceExtensibilityAdapter.ToChangeSet(expansion.Operations));
            snapshot = created.CreateSnapshot();
            await _store.SaveActiveAsync(snapshot, cancellationToken);
        }

        RuntimePerformance performance = RuntimePerformance.Create(snapshot);
        _sourceScene = PerformanceExtensibilityAdapter.ToSourceSceneContext(snapshot);
        _extension = ResolveExtension(new ModuleId(snapshot.SourceScene.ModuleId), new ModuleVersion(snapshot.SourceScene.ModuleVersion));
        _workspace.Initialize(performance);
        _savedStateId = snapshot.StateId;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await CancelAndDrainAutoSaveAsync();
        if (IsDirty)
            await SaveCoreAsync(cancellationToken);
    }

    internal PerformanceSnapshot CreateSnapshot() => Read(value => value.CreateSnapshot());

    internal async Task<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(long randomSeed, CancellationToken cancellationToken)
    {
        RuntimePerformance performance = RequirePerformance();
        IReadOnlyList<BeatDefinition> definitions = await RequireExtension().GetBeatDefinitionsAsync(new BeatDefinitionContext
        {
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            RandomSeed = randomSeed,
            Parameters = _extensions.GetParameters(new ModuleId(RequireSourceScene().Scene.Module.Value))
        }, cancellationToken);
        foreach (BeatDefinition definition in definitions)
            ValidateDefinition(definition, performance.StateId);
        return definitions.Select(value => value with { SourcePerformanceStateId = performance.StateId }).ToArray();
    }

    internal PerformanceCommitResult CreateBeat(BeatDefinition definition, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinition(definition, expectedStateId);
        return Commit(new PerformanceChangeSet(
        [
            new AddBeatOperation(
                Guid.NewGuid(),
                new BeatDefinitionType(definition.Id.Value),
                definition.Module.Value,
                definition.ModuleVersion.Value,
                expectedStateId,
                definition.Name,
                definition.Description,
                definition.Slots.Select(value => new BeatSlotSpecification(value.Id, value.Name, value.Description, value.Minimum, value.Maximum)).ToArray(),
                _extensions.FindWritingProfile(definition.Module)?.GetRawText())
        ]), expectedStateId);
    }

    internal PerformanceCommitResult SetBeatBinding(Guid beatId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new SetBeatSlotBindingOperation(beatId, new BeatSlotBinding(slotId, elementIds))]), expectedStateId);

    internal PerformanceCommitResult ClearBeatBinding(Guid beatId, string slotId, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new ClearBeatSlotBindingOperation(beatId, slotId)]), expectedStateId);

    internal PerformanceCommitResult BeginBeatProcessing(Guid beatId, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new BeginBeatProcessingOperation(beatId)]), expectedStateId);

    internal async Task<PerformanceCommitResult> ResolveBeatAsync(Guid beatId, string interaction, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken)
    {
        RuntimePerformance performance = RequirePerformance();
        if (performance.StateId != expectedStateId)
            throw new OptimisticConcurrencyConflictException(expectedStateId, performance.StateId);
        PerformanceSnapshot captured = performance.CreateSnapshot();
        Beat beat = captured.Beats.TryGetValue(beatId, out Beat? capturedBeat)
            ? capturedBeat
            : throw new KeyNotFoundException($"Performance 中不存在 Beat {beatId}。");
        BeatResolutionProposal proposal = await RequireExtension().ResolveBeatAsync(new BeatResolutionContext
        {
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            Beat = PerformanceExtensibilityAdapter.ToView(beat),
            Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction)),
            RandomSeed = randomSeed,
            Parameters = _extensions.GetParameters(RequireSourceScene().Scene.Module)
        }, cancellationToken);
        if (proposal.ExpectedPerformanceStateId != expectedStateId)
            throw new InvalidOperationException("Beat 解决提案未基于请求的 Performance StateId。");
        IReadOnlyList<BeatParagraph> paragraphs = await _narration.RenderAsync(
            captured,
            beat,
            proposal,
            interaction,
            cancellationToken);
        if (StateId != captured.StateId)
            throw new OptimisticConcurrencyConflictException(captured.StateId, StateId);
        return Commit(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(
                beatId,
                PerformanceExtensibilityAdapter.ToChangeSet(proposal.Operations),
                paragraphs)
        ]), expectedStateId);
    }

    internal PerformanceCommitResult PublishBeat(Guid beatId, Guid expectedStateId, Guid expectedManuscriptStateId)
    {
        Beat beat = Read(value =>
        {
            if (value.StateId != expectedStateId)
                throw new OptimisticConcurrencyConflictException(expectedStateId, value.StateId);
            return value.GetBeat(beatId);
        });
        if (beat.State != BeatState.Resolved)
            throw new InvalidOperationException($"Beat {beatId} 尚未解决。");
        BeatPublicationResult publication = _beatPublisher.PublishBeat(Id, beatId, beat.Paragraphs, expectedManuscriptStateId);
        PerformanceCommitResult result = Commit(new PerformanceChangeSet(
        [
            new MarkBeatPublishedOperation(beatId, new BeatPublicationReceipt(publication.ManuscriptId, publication.ManuscriptStateId))
        ]), expectedStateId);
        RuntimePerformance checkpoint = RequirePerformance();
        _workspace.Reset(checkpoint);
        return result;
    }

    internal async Task<SceneSettlementProposal> PrepareSettlementAsync(CancellationToken cancellationToken)
    {
        RuntimePerformance performance = RequirePerformance();
        Guid capturedStateId = performance.StateId;
        if (performance.Status != PerformanceStatus.Active || performance.GetBeats().Count == 0 || performance.GetBeats().Any(value => value.State != BeatState.Published))
            throw new InvalidOperationException("只有全部 Beat 已发布的活动 Performance 才能准备 Scene 结算。");
        SceneSettlementProposal proposal = await RequireExtension().CompleteAsync(new PerformanceCompletionContext
        {
            Scene = RequireSourceScene(),
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            Parameters = _extensions.GetParameters(RequireSourceScene().Scene.Module)
        }, cancellationToken);
        if (proposal.ExpectedScenarioStateId != RequireSourceScene().ScenarioStateId)
            throw new InvalidOperationException("Performance 结算提案未基于冻结 Scene 的 Scenario StateId。");
        if (StateId != capturedStateId)
            throw new OptimisticConcurrencyConflictException(capturedStateId, StateId);
        return proposal;
    }

    internal PerformanceCommitResult Complete(Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new CompletePerformanceOperation()]), expectedStateId);

    internal PerformanceCommitResult Abandon(Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new AbandonPerformanceOperation()]), expectedStateId);

    internal PerformanceCommitResult Undo(Guid expectedStateId)
    {
        if (!_workspace.CanUndo)
            throw new InvalidOperationException("没有可撤销的 Performance 操作。");
        PerformanceCommitResult result = Convert(_workspace.Undo(expectedStateId));
        Publish(result);
        return result;
    }

    internal PerformanceCommitResult Redo(Guid expectedStateId)
    {
        if (!_workspace.CanRedo)
            throw new InvalidOperationException("没有可重做的 Performance 操作。");
        PerformanceCommitResult result = Convert(_workspace.Redo(expectedStateId));
        Publish(result);
        return result;
    }

    internal async Task SaveAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await CancelAndDrainAutoSaveAsync();
        await SaveCoreAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        await CancelAndDrainAutoSaveAsync();
        try
        {
            if (_workspace.IsInitialized && IsDirty)
                await SaveCoreAsync(CancellationToken.None);
        }
        finally
        {
            _disposed = true;
            _saveGate.Dispose();
        }
    }

    private PerformanceCommitResult Commit(PerformanceChangeSet changes, Guid expectedStateId)
    {
        PerformanceCommitResult result = Convert(_workspace.Commit(changes, expectedStateId));
        Publish(result);
        return result;
    }

    private void Publish(PerformanceCommitResult result)
    {
        if (!result.Changed)
            return;
        ScheduleAutoSave();
        Changed?.Invoke(this, new PerformanceSessionChangedEventArgs(Id, result));
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        PerformanceSnapshot snapshot = CreateSnapshot();
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveActiveAsync(snapshot, cancellationToken);
            _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = snapshot.StateId;
                LastAutoSaveException = null;
                return true;
            });
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ScheduleAutoSave()
    {
        if (_disposed || Health != PerformanceSessionHealth.Healthy)
            return;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
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
            await SaveCoreAsync(source.Token);
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

    private TResult Read<TResult>(Func<RuntimePerformance, TResult> reader)
    {
        ThrowIfDisposed();
        return _workspace.Read(reader);
    }

    private RuntimePerformance RequirePerformance() => Read(value => RuntimePerformance.Create(value.CreateSnapshot()));
    private IPerformanceExtension RequireExtension() => _extension ?? throw new InvalidOperationException("PerformanceSession 尚未初始化。");
    private SceneContextView RequireSourceScene() => _sourceScene ?? throw new InvalidOperationException("PerformanceSession 尚未初始化。");

    private IPerformanceExtension ResolveExtension(ModuleId module, ModuleVersion version)
    {
        IPerformanceExtension extension = _extensions.PerformanceExtensions.SingleOrDefault(value => value.Module == module).Extension
            ?? throw new InvalidOperationException($"Module {module} 未提供 Performance 能力。");
        if (_extensions.FindPlugin(module)?.Version != version)
            throw new InvalidOperationException("Performance Module 版本与冻结 Scene 不一致。");
        return extension;
    }

    private void ValidateDefinition(BeatDefinition definition, Guid stateId)
    {
        SceneContextView scene = RequireSourceScene();
        if (definition.Module != scene.Scene.Module || definition.ModuleVersion != scene.Scene.ModuleVersion)
            throw new InvalidOperationException("BeatDefinition Module 身份与 Performance 不一致。");
        if (definition.SourcePerformanceStateId is Guid source && source != stateId)
            throw new InvalidOperationException("BeatDefinition 已经过期。");
        if (definition.Slots.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != definition.Slots.Count)
            throw new InvalidOperationException("BeatDefinition 包含重复槽位。");
    }

    private static PerformanceCommitResult Convert(VersionedCommitResult<AppliedPerformanceChangeSet> result)
        => result.Changed
            ? new PerformanceCommitResult(result.CommitId, result.PreviousStateId, result.StateId, result.History)
            : PerformanceCommitResult.Unchanged(result.StateId);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
