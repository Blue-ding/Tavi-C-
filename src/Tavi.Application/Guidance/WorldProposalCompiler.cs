using Tavi.Application.World;
using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>表示 WorldProposal 编译为确定性领域操作后的结果。</summary>
/// <param name="ChangeSet">按 Element、Scope、规则断言、本地语义依赖顺序排列的操作组。</param>
/// <param name="ElementIds">临时 Element 标识到真实标识的映射。</param>
/// <param name="ScopeIds">临时 Scope 标识到真实标识的映射。</param>
/// <param name="OperationChangeIds">与 ChangeSet 操作顺序对齐的原始提案修改标识。</param>
public sealed record ProposalCompilationResult(WorldChangeSet ChangeSet, IReadOnlyDictionary<ProposalElementId, Guid> ElementIds, IReadOnlyDictionary<ProposalScopeId, Guid> ScopeIds, IReadOnlyList<string> OperationChangeIds);

/// <summary>将玩家接受的提案修改解析为真实 ID 和确定性 WorldOperation；编译只读取 World，不执行提交。</summary>
public static class WorldProposalCompiler
{
    /// <summary>编译指定提案；被接受的断言必须同时接受其引用的临时 Element 和 Scope。</summary>
    public static ProposalCompilationResult Compile(WorldProposal proposal, IEnumerable<string> acceptedChangeIds, IWorldWorkspace worldWorkspace)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);
        ArgumentNullException.ThrowIfNull(worldWorkspace);
        string[] acceptedIds = acceptedChangeIds.ToArray();
        HashSet<string> accepted = acceptedIds.ToHashSet(StringComparer.Ordinal);
        if (accepted.Count != acceptedIds.Length)
            throw new ArgumentException("接受的提案修改标识不能重复。", nameof(acceptedChangeIds));
        Dictionary<string, ProposalChange> changesById = proposal.Changes.ToDictionary(change => change.Id, StringComparer.Ordinal);
        string? missingChangeId = accepted.FirstOrDefault(changeId => !changesById.ContainsKey(changeId));
        if (missingChangeId is not null)
            throw new ArgumentException($"提案中不存在修改“{missingChangeId}”。", nameof(acceptedChangeIds));
        ProposeAddElement[] elements = proposal.Changes.OfType<ProposeAddElement>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddScope[] scopes = proposal.Changes.OfType<ProposeAddScope>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddAspect[] aspects = proposal.Changes.OfType<ProposeAddAspect>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddRelation[] relations = proposal.Changes.OfType<ProposeAddRelation>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddLocalAspect[] localAspects = proposal.Changes.OfType<ProposeAddLocalAspect>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddLocalRelation[] localRelations = proposal.Changes.OfType<ProposeAddLocalRelation>().Where(change => accepted.Contains(change.Id)).ToArray();
        var elementIds = elements.ToDictionary(change => change.ElementId, change => change.ElementId.Value);
        var scopeIds = scopes.ToDictionary(change => change.ScopeId, change => change.ScopeId.Value);
        WorldSnapshot projectedWorld = worldWorkspace.CreateStagingSnapshot().ProjectedWorld;
        var compiled = new List<(WorldOperation Operation, string ChangeId)>(elements.Length + scopes.Length + aspects.Length + relations.Length + localAspects.Length + localRelations.Length);
        compiled.AddRange(elements.Select(change => ((WorldOperation)new AddElementOperation(elementIds[change.ElementId], change.Name, change.Description, change.Type), change.Id)));
        compiled.AddRange(scopes.Select(change => ((WorldOperation)new AddScopeOperation(scopeIds[change.ScopeId], change.Quantity, change.Type, ResolveElement(change.Owner, elementIds, projectedWorld)), change.Id)));
        compiled.AddRange(aspects.Select(change => ((WorldOperation)new AddAspectOperation(Guid.NewGuid(), change.Quantity, change.Type, ResolveElement(change.Element, elementIds, projectedWorld), ResolveScope(change.Scope, scopeIds, projectedWorld)), change.Id)));
        compiled.AddRange(relations.Select(change => ((WorldOperation)new AddRelationOperation(Guid.NewGuid(), change.Quantity, change.Type, ResolveElement(change.Source, elementIds, projectedWorld), ResolveElement(change.Target, elementIds, projectedWorld), ResolveScope(change.Scope, scopeIds, projectedWorld)), change.Id)));
        compiled.AddRange(localAspects.Select(change => ((WorldOperation)new AddLocalAspectOperation(Guid.NewGuid(), change.Name, change.Description, change.Quantity, ResolveElement(change.Element, elementIds, projectedWorld), ResolveScope(change.Scope, scopeIds, projectedWorld)), change.Id)));
        compiled.AddRange(localRelations.Select(change => ((WorldOperation)new AddLocalRelationOperation(Guid.NewGuid(), change.Name, change.Description, change.Quantity, ResolveElement(change.Source, elementIds, projectedWorld), ResolveElement(change.Target, elementIds, projectedWorld), ResolveScope(change.Scope, scopeIds, projectedWorld)), change.Id)));
        return new ProposalCompilationResult(new WorldChangeSet(compiled.Select(item => item.Operation)), elementIds, scopeIds, compiled.Select(item => item.ChangeId).ToArray());
    }

    private static Guid ResolveElement(ProposalElementReference reference, IReadOnlyDictionary<ProposalElementId, Guid> elementIds, WorldSnapshot world) => reference switch
    {
        ProposalElementReference.Existing existing when world.Elements.ContainsKey(existing.ElementId) => existing.ElementId,
        ProposalElementReference.Existing existing => throw new InvalidOperationException($"Element {existing.ElementId} 不存在于当前临时 World。"),
        ProposalElementReference.Proposed proposed when elementIds.TryGetValue(proposed.ElementId, out Guid id) => id,
        ProposalElementReference.Proposed proposed => throw new InvalidOperationException($"引用的临时 Element {proposed.ElementId.Value} 未被接受。"),
        _ => throw new ArgumentOutOfRangeException(nameof(reference))
    };

    private static Guid ResolveScope(ProposalScopeReference reference, IReadOnlyDictionary<ProposalScopeId, Guid> scopeIds, WorldSnapshot world) => reference switch
    {
        ProposalScopeReference.Existing existing when world.Scopes.ContainsKey(existing.ScopeId) => existing.ScopeId,
        ProposalScopeReference.Existing existing => throw new InvalidOperationException($"Scope {existing.ScopeId} 不存在于当前临时 World。"),
        ProposalScopeReference.Proposed proposed when scopeIds.TryGetValue(proposed.ScopeId, out Guid id) => id,
        ProposalScopeReference.Proposed proposed => throw new InvalidOperationException($"引用的临时 Scope {proposed.ScopeId.Value} 未被接受。"),
        _ => throw new ArgumentOutOfRangeException(nameof(reference))
    };
}
