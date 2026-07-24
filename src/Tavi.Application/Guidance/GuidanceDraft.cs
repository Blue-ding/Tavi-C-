using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>维护单次 GuidanceOperation 隔离的提案构造缓冲区；失败的工具调用不会污染已发布状态。</summary>
internal sealed class GuidanceDraft
{
    private readonly object _sync = new();
    private readonly Dictionary<ProposalElementId, ProposeAddElement> _elements = new();
    private readonly Dictionary<ProposalScopeId, ProposeAddScope> _scopes = new();
    private readonly List<ProposalChange> _changes = [];
    private string _summary = string.Empty;

    internal GuidanceDraft(Guid baseWorldStateId, Guid proposalId)
    {
        if (baseWorldStateId == Guid.Empty)
            throw new ArgumentException("提案基于的 World 状态标识不能为空。", nameof(baseWorldStateId));
        if (proposalId == Guid.Empty)
            throw new ArgumentException("提案标识不能为空。", nameof(proposalId));
        BaseWorldStateId = baseWorldStateId;
        ProposalId = proposalId;
    }

    internal Guid BaseWorldStateId { get; }
    internal Guid ProposalId { get; }

    internal void SetSummary(string summary)
    {
        lock (_sync)
            _summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    internal ProposeAddElement ProposeElement(string rationale, string name, string description, ElementType type)
    {
        var change = new ProposeAddElement(NewChangeId(), rationale ?? string.Empty, ProposalElementId.New(), name, description, type);
        lock (_sync)
        {
            _elements.Add(change.ElementId, change);
            _changes.Add(change);
            return change;
        }
    }

    internal ProposeAddScope ProposeScope(string rationale, string name, string description, double quantity, ScopeType type, ProposalElementReference owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            EnsureKnownReference(owner);
            var change = new ProposeAddScope(NewChangeId(), rationale ?? string.Empty, ProposalScopeId.New(), name, description, quantity, type, owner);
            _scopes.Add(change.ScopeId, change);
            _changes.Add(change);
            return change;
        }
    }

    internal ProposeAddAspect ProposeAspect(string rationale, string name, string description, double quantity, AspectType type, ProposalElementReference element, ProposalScopeReference scope)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_sync)
        {
            EnsureKnownReference(element);
            EnsureKnownReference(scope);
            var change = new ProposeAddAspect(NewChangeId(), rationale ?? string.Empty, name, description, quantity, type, element, scope);
            _changes.Add(change);
            return change;
        }
    }

    internal ProposeAddRelation ProposeRelation(string rationale, string name, string description, double quantity, RelationType type, ProposalElementReference source, ProposalElementReference target, ProposalScopeReference scope)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        lock (_sync)
        {
            EnsureKnownReference(source);
            EnsureKnownReference(target);
            EnsureKnownReference(scope);
            var change = new ProposeAddRelation(NewChangeId(), rationale ?? string.Empty, name, description, quantity, type, source, target, scope);
            _changes.Add(change);
            return change;
        }
    }

    internal WorldProposal CreateProposal()
    {
        lock (_sync)
            return new WorldProposal { Id = ProposalId, BaseWorldStateId = BaseWorldStateId, Summary = _summary, Changes = Array.AsReadOnly(_changes.ToArray()) };
    }

    private void EnsureKnownReference(ProposalElementReference reference)
    {
        if (reference is ProposalElementReference.Proposed proposed && !_elements.ContainsKey(proposed.ElementId))
            throw new ArgumentException($"临时 Element {proposed.ElementId.Value} 不属于当前 GuidanceOperation。", nameof(reference));
    }

    private void EnsureKnownReference(ProposalScopeReference reference)
    {
        if (reference is ProposalScopeReference.Proposed proposed && !_scopes.ContainsKey(proposed.ScopeId))
            throw new ArgumentException($"临时 Scope {proposed.ScopeId.Value} 不属于当前 GuidanceOperation。", nameof(reference));
    }

    private static string NewChangeId() => Guid.NewGuid().ToString("N");
}
