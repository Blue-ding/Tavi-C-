using Tavi.Domain.Performance;
using Tavi.Extensibility;

namespace Tavi.Application.Performance;

/// <summary>提供通过 PerformanceSession 同步和事务边界推进当前 Performance 的命令。</summary>
public sealed class PerformanceCommands
{
    private readonly PerformanceSession _session;

    internal PerformanceCommands(PerformanceSession session) => _session = session;

    public Task<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default)
        => _session.GetBeatDefinitionsAsync(randomSeed, cancellationToken);

    public PerformanceCommitResult CreateBeat(BeatDefinition definition, Guid expectedStateId)
        => _session.CreateBeat(definition, expectedStateId);

    public PerformanceCommitResult SetBeatBinding(Guid beatId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId)
        => _session.SetBeatBinding(beatId, slotId, elementIds, expectedStateId);

    public PerformanceCommitResult ClearBeatBinding(Guid beatId, string slotId, Guid expectedStateId)
        => _session.ClearBeatBinding(beatId, slotId, expectedStateId);

    public PerformanceCommitResult BeginBeatProcessing(Guid beatId, Guid expectedStateId)
        => _session.BeginBeatProcessing(beatId, expectedStateId);

    public Task<PerformanceCommitResult> ResolveBeatAsync(Guid beatId, string interaction, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default)
        => _session.ResolveBeatAsync(beatId, interaction, randomSeed, expectedStateId, cancellationToken);

    public PerformanceCommitResult PublishBeat(Guid beatId, Guid expectedStateId, Guid expectedManuscriptStateId)
        => _session.PublishBeat(beatId, expectedStateId, expectedManuscriptStateId);

    public Task<SceneSettlementProposal> PrepareSettlementAsync(CancellationToken cancellationToken = default)
        => _session.PrepareSettlementAsync(cancellationToken);

    public PerformanceCommitResult Complete(Guid expectedStateId) => _session.Complete(expectedStateId);
    public PerformanceCommitResult Abandon(Guid expectedStateId) => _session.Abandon(expectedStateId);
    public PerformanceCommitResult Undo(Guid expectedStateId) => _session.Undo(expectedStateId);
    public PerformanceCommitResult Redo(Guid expectedStateId) => _session.Redo(expectedStateId);
    public Task SaveAsync(CancellationToken cancellationToken = default) => _session.SaveAsync(cancellationToken);
}
