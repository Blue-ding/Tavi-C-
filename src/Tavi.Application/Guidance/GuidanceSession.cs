using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>
/// 指定 GuidanceSession 生命周期状态。
/// </summary>
public enum GuidanceSessionState
{
    Draft,
    Completed,
    Cancelled
}

/// <summary>
/// 维护一次 Guidance 构筑过程中的临时 Anchor 缓存和提案修改。该会话不直接持有或修改真实 World。
/// </summary>
public sealed class GuidanceSession
{
    private readonly object _sync = new();
    private readonly Dictionary<ProposalAnchorId, ProposeAddAnchor> _anchors = new();
    private readonly List<ProposalChange> _changes = [];
    private string _summary = string.Empty;

    /// <summary>
    /// 创建基于指定 World revision 的 GuidanceSession。
    /// </summary>
    public GuidanceSession(long baseWorldRevision)
    {
        if (baseWorldRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(baseWorldRevision));
        Id = Guid.NewGuid();
        BaseWorldRevision = baseWorldRevision;
    }

    /// <summary>
    /// 获取 GuidanceSession 标识。
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 获取本次构筑开始时的 World revision。
    /// </summary>
    public long BaseWorldRevision { get; }

    /// <summary>
    /// 获取当前生命周期状态。
    /// </summary>
    public GuidanceSessionState State { get; private set; } = GuidanceSessionState.Draft;

    /// <summary>
    /// 更新面向玩家的提案摘要。
    /// </summary>
    public void SetSummary(string summary)
    {
        lock (_sync)
        {
            EnsureDraft();
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        }
    }

    /// <summary>
    /// 添加临时 Anchor，并返回可供后续 Relation 引用的强类型临时标识。
    /// </summary>
    public ProposalAnchorId ProposeAnchor(string changeId, string rationale, string name, string description, AnchorType type)
    {
        EnsureChangeId(changeId);
        var change = new ProposeAddAnchor(changeId, rationale ?? string.Empty, ProposalAnchorId.New(), name, description, type);
        lock (_sync)
        {
            EnsureDraft();
            EnsureUniqueChangeId(changeId);
            _anchors.Add(change.AnchorId, change);
            _changes.Add(change);
            return change.AnchorId;
        }
    }

    /// <summary>
    /// 添加 Relation 提案；所有临时 Anchor 引用必须属于当前 GuidanceSession。
    /// </summary>
    public void ProposeRelation(string changeId, string rationale, string name, string description, ProposalAnchorReference source, ProposalAnchorReference target, ProposedRelationScope scope)
    {
        EnsureChangeId(changeId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_sync)
        {
            EnsureDraft();
            EnsureUniqueChangeId(changeId);
            EnsureKnownReference(source);
            EnsureKnownReference(target);
            if (scope is ProposedRelationScope.SubWorld subWorld)
                EnsureKnownReference(subWorld.Character);
            _changes.Add(new ProposeAddRelation(changeId, rationale ?? string.Empty, name, description, source, target, scope));
        }
    }

    /// <summary>
    /// 创建与后续会话修改完全分离的 WorldProposal 快照。
    /// </summary>
    public WorldProposal CreateProposal()
    {
        lock (_sync)
        {
            return new WorldProposal { Id = Id, BaseWorldRevision = BaseWorldRevision, Summary = _summary, Changes = Array.AsReadOnly(_changes.ToArray()) };
        }
    }

    /// <summary>
    /// 将会话标记为已完成；完成后不能继续修改提案。
    /// </summary>
    public void Complete()
    {
        lock (_sync)
        {
            EnsureDraft();
            State = GuidanceSessionState.Completed;
        }
    }

    /// <summary>
    /// 取消会话；取消后不能继续修改提案。
    /// </summary>
    public void Cancel()
    {
        lock (_sync)
        {
            EnsureDraft();
            State = GuidanceSessionState.Cancelled;
        }
    }

    private void EnsureKnownReference(ProposalAnchorReference reference)
    {
        if (reference is ProposalAnchorReference.Proposed proposed && !_anchors.ContainsKey(proposed.AnchorId))
            throw new ArgumentException($"临时 Anchor {proposed.AnchorId.Value} 不属于当前 GuidanceSession。", nameof(reference));
    }

    private void EnsureUniqueChangeId(string changeId)
    {
        if (_changes.Any(change => string.Equals(change.Id, changeId, StringComparison.Ordinal)))
            throw new ArgumentException($"提案修改标识“{changeId}”重复。", nameof(changeId));
    }

    private void EnsureDraft()
    {
        if (State != GuidanceSessionState.Draft)
            throw new InvalidOperationException($"GuidanceSession 当前状态为 {State}，不能继续修改。");
    }

    private static void EnsureChangeId(string changeId)
    {
        if (string.IsNullOrWhiteSpace(changeId))
            throw new ArgumentException("提案修改标识不能为空。", nameof(changeId));
    }
}
