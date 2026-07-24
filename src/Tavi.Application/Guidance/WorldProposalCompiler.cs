using Tavi.Application.World;
using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>
/// 表示 WorldProposal 编译为确定性领域操作后的结果。
/// </summary>
public sealed record ProposalCompilationResult(WorldChangeSet ChangeSet, IReadOnlyDictionary<ProposalAnchorId, Guid> AnchorIds);

/// <summary>
/// 将玩家接受的提案修改解析为真实 ID 和确定性 WorldOperation；编译只读取 World，不执行提交。
/// </summary>
public static class WorldProposalCompiler
{
    /// <summary>
    /// 编译指定提案。Relation 引用的临时 Anchor 必须同时被接受；现有 Anchor 和 SubWorld Character 会通过 World 服务查询边界校验。
    /// </summary>
    public static ProposalCompilationResult Compile(WorldProposal proposal, IEnumerable<string> acceptedChangeIds, IWorldService worldService)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);
        ArgumentNullException.ThrowIfNull(worldService);
        string[] acceptedIds = acceptedChangeIds.ToArray();
        HashSet<string> accepted = acceptedIds.ToHashSet(StringComparer.Ordinal);
        if (accepted.Count != acceptedIds.Length)
            throw new ArgumentException("接受的提案修改标识不能重复。", nameof(acceptedChangeIds));
        Dictionary<string, ProposalChange> changesById = proposal.Changes.ToDictionary(change => change.Id, StringComparer.Ordinal);
        string? missingChangeId = accepted.FirstOrDefault(changeId => !changesById.ContainsKey(changeId));
        if (missingChangeId is not null)
            throw new ArgumentException($"提案中不存在修改“{missingChangeId}”。", nameof(acceptedChangeIds));

        ProposeAddAnchor[] anchors = proposal.Changes.OfType<ProposeAddAnchor>().Where(change => accepted.Contains(change.Id)).ToArray();
        ProposeAddRelation[] relations = proposal.Changes.OfType<ProposeAddRelation>().Where(change => accepted.Contains(change.Id)).ToArray();
        var anchorIds = anchors.ToDictionary(change => change.AnchorId, change => change.AnchorId.Value);
        WorldSnapshot projectedWorld = worldService.CreateStagingSnapshot().ProjectedWorld;
        var operations = new List<WorldOperation>(anchors.Length + relations.Length);
        operations.AddRange(anchors.Select(change => new AddAnchorOperation(anchorIds[change.AnchorId], change.Name, change.Description, change.Type)));
        foreach (ProposeAddRelation relation in relations)
        {
            Guid sourceId = ResolveAnchor(relation.Source, anchorIds, projectedWorld);
            Guid targetId = ResolveAnchor(relation.Target, anchorIds, projectedWorld);
            Guid? domainId = relation.Scope switch
            {
                ProposedRelationScope.World => null,
                ProposedRelationScope.SubWorld subWorld => RequireCharacter(ResolveAnchor(subWorld.Character, anchorIds, projectedWorld), anchors, anchorIds, projectedWorld),
                _ => throw new ArgumentOutOfRangeException(nameof(relation.Scope))
            };
            operations.Add(WorldOperations.AddRelation(relation.Name, relation.Description, sourceId, targetId, domainId));
        }
        return new ProposalCompilationResult(new WorldChangeSet(operations), anchorIds);
    }

    private static Guid ResolveAnchor(ProposalAnchorReference reference, IReadOnlyDictionary<ProposalAnchorId, Guid> anchorIds, WorldSnapshot world)
    {
        return reference switch
        {
            ProposalAnchorReference.Existing existing => existing.AnchorId,
            ProposalAnchorReference.Proposed proposed when anchorIds.TryGetValue(proposed.AnchorId, out Guid anchorId) => anchorId,
            ProposalAnchorReference.Proposed proposed => throw new InvalidOperationException($"Relation 引用的临时 Anchor {proposed.AnchorId.Value} 未被接受。"),
            _ => throw new ArgumentOutOfRangeException(nameof(reference))
        };
    }

    private static Guid RequireCharacter(Guid anchorId, IReadOnlyCollection<ProposeAddAnchor> proposedAnchors, IReadOnlyDictionary<ProposalAnchorId, Guid> anchorIds, WorldSnapshot world)
    {
        ProposeAddAnchor? proposed = proposedAnchors.SingleOrDefault(change => anchorIds.GetValueOrDefault(change.AnchorId) == anchorId);
        AnchorType? type = proposed?.Type ?? world.Anchors.GetValueOrDefault(anchorId)?.Type;
        if (type.HasValue && type != AnchorType.Character)
            throw new InvalidOperationException($"Anchor {anchorId} 不是 Character，不能持有子世界。");
        return anchorId;
    }
}
