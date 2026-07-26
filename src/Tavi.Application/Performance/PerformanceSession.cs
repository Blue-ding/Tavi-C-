using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using Tavi.Utilities.Concurrency;
using RuntimePerformance = Tavi.Domain.Performance.Performance;

namespace Tavi.Application.Performance;

/// <summary>持有一次 Scene 的 Performance，并协调 Module 规则与 Writing Beat 发布。</summary>
public sealed class PerformanceSession : IPerformanceService
{
    private readonly SceneContextView _scene;
    private readonly IWritingService _writing;
    private readonly FrozenModuleRuntime _extensions;
    private readonly VersionedWorkspace<RuntimePerformance, PerformanceChangeSet, AppliedPerformanceChangeSet> _workspace = new(new PerformanceConcurrencyModel());
    private IPerformanceExtension? _extension;

    public PerformanceSession(SceneContextView scene, IWritingService writing, FrozenModuleRuntime extensions)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _writing = writing ?? throw new ArgumentNullException(nameof(writing));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        if (!string.Equals(scene.Scene.State, "Processing", StringComparison.Ordinal))
            throw new ArgumentException("Performance 只能接收 Processing Scene。", nameof(scene));
    }

    public Guid StateId => _workspace.StateId;
    public PerformanceStatus Status => Read(value => value.Status);

    public async Task InitializeAsync(long randomSeed, CancellationToken cancellationToken = default)
    {
        if (_workspace.IsInitialized)
            throw new InvalidOperationException("PerformanceSession 已经初始化。");
        _extension = _extensions.PerformanceExtensions.SingleOrDefault(value => value.Module == _scene.Scene.Module).Extension
            ?? throw new InvalidOperationException($"Module {_scene.Scene.Module} 未提供 Performance 能力。");
        if (_extensions.FindPlugin(_scene.Scene.Module)?.Version != _scene.Scene.ModuleVersion)
            throw new InvalidOperationException("Performance Module 版本与冻结 Scene 不一致。");
        PerformanceExpansionProposal expansion = await _extension.ExpandAsync(new PerformanceExpansionContext
        {
            Scene = _scene,
            RandomSeed = randomSeed,
            Parameters = _extensions.GetParameters(_scene.Scene.Module)
        }, cancellationToken);
        if (expansion.ExpectedScenarioStateId != _scene.ScenarioStateId)
            throw new InvalidOperationException("Performance 展开提案未基于冻结 Scene 的 Scenario StateId。");
        RuntimePerformance performance = RuntimePerformance.Create(PerformanceExtensibilityAdapter.CreateSeed(_scene, _extensions));
        _ = performance.Apply(PerformanceExtensibilityAdapter.ToChangeSet(expansion.Operations));
        _workspace.Initialize(performance);
    }

    public PerformanceSnapshot GetSnapshot() => Read(value => value.CreateSnapshot());

    public async Task<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default)
    {
        RuntimePerformance performance = RequirePerformance();
        IReadOnlyList<BeatDefinition> definitions = await RequireExtension().GetBeatDefinitionsAsync(new BeatDefinitionContext
        {
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            RandomSeed = randomSeed,
            Parameters = _extensions.GetParameters(_scene.Scene.Module)
        }, cancellationToken);
        foreach (BeatDefinition definition in definitions)
            ValidateDefinition(definition, performance.StateId);
        return definitions.Select(value => value with { SourcePerformanceStateId = performance.StateId }).ToArray();
    }

    public PerformanceCommitResult CreateBeat(BeatDefinition definition, Guid expectedStateId)
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
                definition.Slots.Select(value => new BeatSlotSpecification(value.Id, value.Name, value.Description, value.Minimum, value.Maximum)).ToArray())
        ]), expectedStateId);
    }

    public PerformanceCommitResult SetBeatBinding(Guid beatId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new SetBeatSlotBindingOperation(beatId, new BeatSlotBinding(slotId, elementIds))]), expectedStateId);

    public PerformanceCommitResult ClearBeatBinding(Guid beatId, string slotId, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new ClearBeatSlotBindingOperation(beatId, slotId)]), expectedStateId);

    public PerformanceCommitResult BeginBeatProcessing(Guid beatId, Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new BeginBeatProcessingOperation(beatId)]), expectedStateId);

    public async Task<PerformanceCommitResult> ResolveBeatAsync(Guid beatId, string interaction, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default)
    {
        RuntimePerformance performance = RequirePerformance();
        if (performance.StateId != expectedStateId)
            throw new OptimisticConcurrencyConflictException(expectedStateId, performance.StateId);
        Beat beat = performance.GetBeat(beatId);
        BeatResolutionProposal proposal = await RequireExtension().ResolveBeatAsync(new BeatResolutionContext
        {
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            Beat = PerformanceExtensibilityAdapter.ToView(beat),
            Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction)),
            RandomSeed = randomSeed,
            Parameters = _extensions.GetParameters(_scene.Scene.Module)
        }, cancellationToken);
        if (proposal.ExpectedPerformanceStateId != expectedStateId)
            throw new InvalidOperationException("Beat 解决提案未基于请求的 Performance StateId。");
        return Commit(new PerformanceChangeSet(
        [
            new ResolveBeatOperation(
                beatId,
                PerformanceExtensibilityAdapter.ToChangeSet(proposal.Operations),
                proposal.Paragraphs.Select(value => new BeatParagraph(value.Id, value.Text)).ToArray())
        ]), expectedStateId);
    }

    public PerformanceCommitResult PublishBeat(Guid beatId, Guid expectedStateId, Guid expectedManuscriptStateId)
    {
        Beat beat = Read(value =>
        {
            if (value.StateId != expectedStateId)
                throw new OptimisticConcurrencyConflictException(expectedStateId, value.StateId);
            return value.GetBeat(beatId);
        });
        if (beat.State != BeatState.Resolved)
            throw new InvalidOperationException($"Beat {beatId} 尚未解决。");
        BeatPublicationResult publication = _writing.PublishBeat(GetPerformanceId(), beatId, beat.Paragraphs, expectedManuscriptStateId);
        return Commit(new PerformanceChangeSet(
        [
            new MarkBeatPublishedOperation(beatId, new BeatPublicationReceipt(publication.ManuscriptId, publication.ManuscriptStateId))
        ]), expectedStateId);
    }

    public async Task<SceneSettlementProposal> PrepareSettlementAsync(CancellationToken cancellationToken = default)
    {
        RuntimePerformance performance = RequirePerformance();
        Guid capturedStateId = performance.StateId;
        if (performance.Status != PerformanceStatus.Active || performance.GetBeats().Count == 0 || performance.GetBeats().Any(value => value.State != BeatState.Published))
            throw new InvalidOperationException("只有全部 Beat 已发布的活动 Performance 才能准备 Scene 结算。");
        SceneSettlementProposal proposal = await RequireExtension().CompleteAsync(new PerformanceCompletionContext
        {
            Scene = _scene,
            Performance = PerformanceExtensibilityAdapter.ToView(performance),
            Parameters = _extensions.GetParameters(_scene.Scene.Module)
        }, cancellationToken);
        if (proposal.ExpectedScenarioStateId != _scene.ScenarioStateId)
            throw new InvalidOperationException("Performance 结算提案未基于冻结 Scene 的 Scenario StateId。");
        Guid currentStateId = StateId;
        if (currentStateId != capturedStateId)
            throw new OptimisticConcurrencyConflictException(capturedStateId, currentStateId);
        return proposal;
    }

    public PerformanceCommitResult Complete(Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new CompletePerformanceOperation()]), expectedStateId);

    public PerformanceCommitResult Abandon(Guid expectedStateId)
        => Commit(new PerformanceChangeSet([new AbandonPerformanceOperation()]), expectedStateId);

    private PerformanceCommitResult Commit(PerformanceChangeSet changes, Guid expectedStateId) => Convert(_workspace.Commit(changes, expectedStateId));
    private TResult Read<TResult>(Func<RuntimePerformance, TResult> reader) => _workspace.Read(reader);
    private RuntimePerformance RequirePerformance() => Read(value => RuntimePerformance.Create(value.CreateSnapshot()));
    private IPerformanceExtension RequireExtension() => _extension ?? throw new InvalidOperationException("PerformanceSession 尚未初始化。");
    // 当前模型保证一个 Processing Scene 同时至多对应一个 Performance，因此 SourceSceneId 是稳定发布键。
    private Guid GetPerformanceId() => Read(value => value.SourceSceneId);

    private void ValidateDefinition(BeatDefinition definition, Guid stateId)
    {
        if (definition.Module != _scene.Scene.Module || definition.ModuleVersion != _scene.Scene.ModuleVersion)
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
}
