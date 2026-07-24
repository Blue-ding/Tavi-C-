using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Infrastructure.Persistence.Tests;

/// <summary>提供只供持久化集成测试组装运行时 World 的简短操作帮助方法。</summary>
internal static class WorldTestExtensions
{
    internal static Guid AddElement(this RuntimeWorld world, string name, string description, ElementType type)
    {
        AddElementOperation operation = WorldOperations.AddElement(name, description, type);
        world.Apply(WorldOperations.Single(operation));
        return operation.ElementId;
    }

    internal static Guid AddScope(this RuntimeWorld world, string name, string description, double quantity, ScopeType type, Guid ownerElementId)
    {
        AddScopeOperation operation = WorldOperations.AddScope(name, description, quantity, type, ownerElementId);
        world.Apply(WorldOperations.Single(operation));
        return operation.ScopeId;
    }

    internal static Guid AddAspect(this RuntimeWorld world, string name, string description, double quantity, AspectType type, Guid elementId, Guid scopeId)
    {
        AddAspectOperation operation = WorldOperations.AddAspect(name, description, quantity, type, elementId, scopeId);
        world.Apply(WorldOperations.Single(operation));
        return operation.AspectId;
    }

    internal static Guid AddRelation(this RuntimeWorld world, string name, string description, double quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId)
    {
        AddRelationOperation operation = WorldOperations.AddRelation(name, description, quantity, type, sourceElementId, targetElementId, scopeId);
        world.Apply(WorldOperations.Single(operation));
        return operation.RelationId;
    }
}
