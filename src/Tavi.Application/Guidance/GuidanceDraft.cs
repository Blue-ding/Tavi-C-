using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>维护单次 GuidanceOperation 隔离的提案构造缓冲区；失败的操作不会污染已发布状态。</summary>
internal sealed class GuidanceDraft
{
    private readonly object _sync = new();
    private readonly Dictionary<ProposalAnchorId, ProposeAddAnchor> _anchors = new();
    private readonly List<ProposalChange> _changes = [];
    private string _summary = string.Empty;

    internal GuidanceDraft(long baseWorldRevision, Guid proposalId)
    {
        if (baseWorldRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(baseWorldRevision));
        if (proposalId == Guid.Empty)
            throw new ArgumentException("提案标识不能为空。", nameof(proposalId));
        BaseWorldRevision = baseWorldRevision;
        ProposalId = proposalId;
    }

    internal long BaseWorldRevision { get; }
    internal Guid ProposalId { get; }

    internal void SetSummary(string summary)
    {
        lock (_sync)
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    internal ProposeAddAnchor ProposeAnchor(string rationale, string name, string description, AnchorType type)
    {
        var change = new ProposeAddAnchor(Guid.NewGuid().ToString("N"), rationale ?? string.Empty, ProposalAnchorId.New(), name, description, type);
        lock (_sync)
        {
            _anchors.Add(change.AnchorId, change);
            _changes.Add(change);
            return change;
        }
    }

    internal ProposeAddRelation ProposeRelation(string rationale, string name, string description, ProposalAnchorReference source, ProposalAnchorReference target, ProposedRelationScope scope)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_sync)
        {
            EnsureKnownReference(source);
            EnsureKnownReference(target);
            if (scope is ProposedRelationScope.SubWorld subWorld)
                EnsureKnownReference(subWorld.Character);
            var change = new ProposeAddRelation(Guid.NewGuid().ToString("N"), rationale ?? string.Empty, name, description, source, target, scope);
            _changes.Add(change);
            return change;
        }
    }

    internal WorldProposal CreateProposal()
    {
        lock (_sync)
            return new WorldProposal { Id = ProposalId, BaseWorldRevision = BaseWorldRevision, Summary = _summary, Changes = Array.AsReadOnly(_changes.ToArray()) };
    }

    private void EnsureKnownReference(ProposalAnchorReference reference)
    {
        if (reference is ProposalAnchorReference.Proposed proposed && !_anchors.ContainsKey(proposed.AnchorId))
            throw new ArgumentException($"临时 Anchor {proposed.AnchorId.Value} 不属于当前 GuidanceOperation。", nameof(reference));
    }
}
