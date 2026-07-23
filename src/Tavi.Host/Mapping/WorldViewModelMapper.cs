using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

internal static class WorldViewModelMapper
{
    internal static WorldGraphViewModel ToGraph(WorldSession session)
    {
        WorldSnapshot snapshot = session.Queries.CreateSnapshot();
        HashSet<Guid> subWorldCharacters = snapshot.SubWorlds.Select(subWorld => subWorld.DomainId).ToHashSet();
        AnchorViewModel[] nodes = snapshot.Anchors.Values.OrderBy(anchor => anchor.Name, StringComparer.OrdinalIgnoreCase).Select(anchor => new AnchorViewModel(anchor.Id, anchor.Name, anchor.Description, anchor.Type.ToString(), subWorldCharacters.Contains(anchor.Id))).ToArray();
        var edges = new List<RelationViewModel>(snapshot.Relations.Count + snapshot.SubWorlds.Sum(subWorld => subWorld.Relations.Count));
        edges.AddRange(snapshot.Relations.Values.Select(relation => ToRelation(relation, null)));
        foreach (SubWorldSnapshot subWorld in snapshot.SubWorlds)
            edges.AddRange(subWorld.Relations.Values.Select(relation => ToRelation(relation, subWorld.DomainId)));
        SubWorldViewModel[] subWorlds = snapshot.SubWorlds.Select(subWorld => new SubWorldViewModel(subWorld.Id, subWorld.DomainId)).ToArray();
        return new WorldGraphViewModel(snapshot.Id, session.Revision, session.IsDirty, session.CanUndo, session.CanRedo, session.Health.ToString(), nodes, edges.OrderBy(edge => edge.Name, StringComparer.OrdinalIgnoreCase).ToArray(), subWorlds);
    }

    internal static WorldCommitViewModel ToCommit(WorldCommitResult result, Guid? entityId = null) => new(result.CommitId, result.PreviousRevision, result.Revision, result.Changed, entityId);

    private static RelationViewModel ToRelation(Relation relation, Guid? domainCharacterId) => new(relation.Id, relation.Name, relation.Description, relation.SourceId, relation.TargetId, domainCharacterId.HasValue ? "SubWorld" : "World", domainCharacterId);
}
