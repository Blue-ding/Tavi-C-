using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Infrastructure.Persistence.Tests;

internal static class WorldTestExtensions
{
    internal static Guid AddAnchor(this RuntimeWorld world, string name, string description, AnchorType type)
    {
        AddAnchorOperation operation = WorldOperations.AddAnchor(name, description, type);
        world.Apply(WorldOperations.Single(operation));
        return operation.AnchorId;
    }

    internal static Guid AddRelation(this RuntimeWorld world, string name, string description, Guid sourceId, Guid targetId, Guid? domainId = null)
    {
        AddRelationOperation operation = WorldOperations.AddRelation(name, description, sourceId, targetId, domainId);
        world.Apply(WorldOperations.Single(operation));
        return operation.RelationId;
    }
}
