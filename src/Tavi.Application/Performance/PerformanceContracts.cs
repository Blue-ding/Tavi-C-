using Tavi.Domain.Performance;
using Tavi.Extensibility;

namespace Tavi.Application.Performance;

public sealed record PerformanceCommitResult(Guid CommitId, Guid PreviousStateId, Guid StateId, AppliedPerformanceChangeSet? ChangeSet)
{
    public bool Changed => ChangeSet is not null;
    public static PerformanceCommitResult Unchanged(Guid stateId) => new(Guid.Empty, stateId, stateId, null);
}

/// <summary>定义单个 Processing Scene 的临时 Performance 演绎接口。</summary>
public interface IPerformanceService
{
    Guid StateId { get; }
    PerformanceStatus Status { get; }

    /// <summary>导入冻结 Scene 上下文并应用所属 Module 的 Performance 展开规则。</summary>
    Task InitializeAsync(long randomSeed, CancellationToken cancellationToken = default);
    PerformanceSnapshot GetSnapshot();
    Task<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default);
    PerformanceCommitResult CreateBeat(BeatDefinition definition, Guid expectedStateId);
    PerformanceCommitResult SetBeatBinding(Guid beatId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId);
    PerformanceCommitResult ClearBeatBinding(Guid beatId, string slotId, Guid expectedStateId);
    PerformanceCommitResult BeginBeatProcessing(Guid beatId, Guid expectedStateId);
    Task<PerformanceCommitResult> ResolveBeatAsync(Guid beatId, string interaction, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default);

    /// <summary>幂等发布 Resolved Beat 的正文，并在成功后标记为 Published。</summary>
    PerformanceCommitResult PublishBeat(Guid beatId, Guid expectedStateId, Guid expectedManuscriptStateId);

    /// <summary>让 Module 根据完整 Performance 生成 Scene 的局部结算提案；不会自行修改 Scenario。</summary>
    Task<SceneSettlementProposal> PrepareSettlementAsync(CancellationToken cancellationToken = default);
    /// <summary>仅在调用方已成功把准备好的提案提交给 Scenario 后确认 Performance 完成。</summary>
    PerformanceCommitResult Complete(Guid expectedStateId);
    PerformanceCommitResult Abandon(Guid expectedStateId);
}
