using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Domain.Tests;

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

    internal static void RemoveAnchor(this RuntimeWorld world, Guid anchorId) => world.Apply(WorldOperations.Single(new RemoveAnchorOperation(anchorId)));
    internal static void RemoveRelation(this RuntimeWorld world, Guid relationId) => world.Apply(WorldOperations.Single(new RemoveRelationOperation(relationId)));
    internal static void UpdateAnchorName(this RuntimeWorld world, Guid anchorId, string name) => world.Apply(WorldOperations.Single(new UpdateAnchorNameOperation(anchorId, name)));
    internal static void UpdateAnchorDescription(this RuntimeWorld world, Guid anchorId, string description) => world.Apply(WorldOperations.Single(new UpdateAnchorDescriptionOperation(anchorId, description)));
    internal static void UpdateAnchorType(this RuntimeWorld world, Guid anchorId, AnchorType type) => world.Apply(WorldOperations.Single(new UpdateAnchorTypeOperation(anchorId, type)));
    internal static void UpdateRelationName(this RuntimeWorld world, Guid relationId, string name) => world.Apply(WorldOperations.Single(new UpdateRelationNameOperation(relationId, name)));
    internal static void UpdateRelationDescription(this RuntimeWorld world, Guid relationId, string description) => world.Apply(WorldOperations.Single(new UpdateRelationDescriptionOperation(relationId, description)));

    internal static Guid CreateSubWorld(this RuntimeWorld world, Guid characterId)
    {
        CreateSubWorldOperation operation = WorldOperations.CreateSubWorld(characterId);
        world.Apply(WorldOperations.Single(operation));
        return operation.SubWorldId;
    }

    internal static void RemoveSubWorld(this RuntimeWorld world, Guid characterId) => world.Apply(WorldOperations.Single(new RemoveSubWorldOperation(characterId)));
}
