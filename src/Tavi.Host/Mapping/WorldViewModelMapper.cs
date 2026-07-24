using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

internal static class WorldViewModelMapper
{
    internal static WorldGraphViewModel ToGraph(WorldSession session)
    {
        WorldStagingSnapshot staging = session.CreateStagingSnapshot();
        WorldSnapshot snapshot = staging.ProjectedWorld;
        HashSet<Guid> subWorldCharacters = snapshot.SubWorlds.Select(subWorld => subWorld.DomainId).ToHashSet();
        AnchorViewModel[] nodes = snapshot.Anchors.Values.OrderBy(anchor => anchor.Name, StringComparer.OrdinalIgnoreCase).Select(anchor => new AnchorViewModel(anchor.Id, anchor.Name, anchor.Description, anchor.Type.ToString(), subWorldCharacters.Contains(anchor.Id))).ToArray();
        var edges = new List<RelationViewModel>(snapshot.Relations.Count + snapshot.SubWorlds.Sum(subWorld => subWorld.Relations.Count));
        edges.AddRange(snapshot.Relations.Values.Select(relation => ToRelation(relation, null)));
        foreach (SubWorldSnapshot subWorld in snapshot.SubWorlds)
            edges.AddRange(subWorld.Relations.Values.Select(relation => ToRelation(relation, subWorld.DomainId)));
        SubWorldViewModel[] subWorlds = snapshot.SubWorlds.Select(subWorld => new SubWorldViewModel(subWorld.Id, subWorld.DomainId)).ToArray();
        WorldStagedChangeViewModel[] changes = staging.Changes.Select(change => new WorldStagedChangeViewModel(change.Id, change.Source.ToString(), change.Status.ToString(), string.Join("；", change.ChangeSet.Operations.Select(Describe)), change.Issue, change.ConflictingChangeIds)).ToArray();
        return new WorldGraphViewModel(snapshot.Id, session.Revision, staging.Revision, session.IsDirty, session.CanUndo, session.CanRedo, session.Health.ToString(), nodes, edges.OrderBy(edge => edge.Name, StringComparer.OrdinalIgnoreCase).ToArray(), subWorlds, changes);
    }

    internal static WorldCommitViewModel ToCommit(WorldCommitResult result, Guid? entityId = null) => new(result.CommitId, result.PreviousRevision, result.Revision, result.Changed, entityId);

    private static RelationViewModel ToRelation(Relation relation, Guid? domainCharacterId) => new(relation.Id, relation.Name, relation.Description, relation.SourceId, relation.TargetId, domainCharacterId.HasValue ? "SubWorld" : "World", domainCharacterId);

    private static string Describe(WorldOperation operation) => operation switch
    {
        AddAnchorOperation value => $"新增 Anchor：{value.Name}",
        RemoveAnchorOperation value => $"删除 Anchor：{value.AnchorId}",
        UpdateAnchorNameOperation value => $"修改 Anchor 名称：{value.AnchorId} → {value.Name}",
        UpdateAnchorDescriptionOperation value => $"修改 Anchor 描述：{value.AnchorId}",
        UpdateAnchorTypeOperation value => $"修改 Anchor 类型：{value.AnchorId} → {value.Type}",
        AddRelationOperation value => $"新增 Relation：{value.Name}",
        RemoveRelationOperation value => $"删除 Relation：{value.RelationId}",
        UpdateRelationNameOperation value => $"修改 Relation 名称：{value.RelationId} → {value.Name}",
        UpdateRelationDescriptionOperation value => $"修改 Relation 描述：{value.RelationId}",
        CreateSubWorldOperation value => $"创建子世界：{value.CharacterId}",
        RemoveSubWorldOperation value => $"删除子世界：{value.CharacterId}",
        _ => operation.GetType().Name
    };
}
