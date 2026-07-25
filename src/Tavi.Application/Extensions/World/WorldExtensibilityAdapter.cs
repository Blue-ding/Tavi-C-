using Tavi.Domain.World;
using Tavi.Extensibility;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.Extensions.World;

/// <summary>在 Domain World 与 Module 公共投影、Intent 之间执行集中转换。</summary>
public static class WorldExtensibilityAdapter
{
    /// <summary>把独立 Domain 快照转换为不会泄漏 Aggregate 的公共只读投影。</summary>
    public static IWorldView ToView(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new WorldView(snapshot);
    }

    /// <summary>把 World Authoring Intent 转换为原子 Domain 操作组并验证完整候选状态。</summary>
    public static WorldChangeSet ToChangeSet(WorldAuthoringProposal proposal, WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (proposal.ExpectedWorldStateId != snapshot.Id)
            throw new ModuleSemanticException("TAVI.MODULE.WORLD.STATE_CONFLICT", $"World Authoring 提案基于状态 {proposal.ExpectedWorldStateId}，当前状态为 {snapshot.Id}。");
        var operations = new List<WorldOperation>();
        foreach (WorldAuthoringIntent intent in proposal.Intents)
            Append(intent, operations);
        var result = new WorldChangeSet(operations);
        RuntimeWorld candidate = RuntimeWorld.Create(snapshot);
        _ = candidate.Apply(result);
        return result;
    }

    private static void Append(WorldAuthoringIntent intent, ICollection<WorldOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(intent);
        switch (intent)
        {
            case WorldAuthoringIntent.AddElement value: operations.Add(new AddElementOperation(value.Id, value.Name, value.Description, new ElementType(value.Type.Value))); break;
            case WorldAuthoringIntent.RemoveElement value: operations.Add(new RemoveElementOperation(value.Id)); break;
            case WorldAuthoringIntent.UpdateElement value:
                operations.Add(new UpdateElementNameOperation(value.Id, value.Name));
                operations.Add(new UpdateElementDescriptionOperation(value.Id, value.Description));
                operations.Add(new UpdateElementTypeOperation(value.Id, new ElementType(value.Type.Value)));
                break;
            case WorldAuthoringIntent.AddScope value: operations.Add(new AddScopeOperation(value.Id, value.Quantity, new ScopeType(value.Type.Value), value.OwnerElementId)); break;
            case WorldAuthoringIntent.RemoveScope value: operations.Add(new RemoveScopeOperation(value.Id)); break;
            case WorldAuthoringIntent.UpdateScope value:
                operations.Add(new UpdateScopeQuantityOperation(value.Id, value.Quantity));
                operations.Add(new UpdateScopeTypeOperation(value.Id, new ScopeType(value.Type.Value)));
                break;
            case WorldAuthoringIntent.AddAspect value: operations.Add(new AddAspectOperation(value.Id, value.Quantity, new AspectType(value.Type.Value), value.ElementId, value.ScopeId)); break;
            case WorldAuthoringIntent.RemoveAspect value: operations.Add(new RemoveAspectOperation(value.Id)); break;
            case WorldAuthoringIntent.UpdateAspect value:
                operations.Add(new UpdateAspectQuantityOperation(value.Id, value.Quantity));
                operations.Add(new UpdateAspectTypeOperation(value.Id, new AspectType(value.Type.Value)));
                break;
            case WorldAuthoringIntent.AddRelation value: operations.Add(new AddRelationOperation(value.Id, value.Quantity, new RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)); break;
            case WorldAuthoringIntent.RemoveRelation value: operations.Add(new RemoveRelationOperation(value.Id)); break;
            case WorldAuthoringIntent.UpdateRelation value:
                operations.Add(new UpdateRelationQuantityOperation(value.Id, value.Quantity));
                operations.Add(new UpdateRelationTypeOperation(value.Id, new RelationType(value.Type.Value)));
                break;
            default: throw new ModuleConfigurationException("TAVI.MODULE.WORLD.INTENT_UNSUPPORTED", $"不支持 World Authoring Intent {intent.GetType().FullName}。");
        }
    }

    private sealed class WorldView : IWorldView
    {
        internal WorldView(WorldSnapshot snapshot)
        {
            StateId = snapshot.Id;
            Elements = Array.AsReadOnly(snapshot.Elements.Values.Select(value => new ElementView(value.Id, value.Name, value.Description, new SemanticKey(value.Type.Value))).ToArray());
            Scopes = Array.AsReadOnly(snapshot.Scopes.Values.Select(value => new ScopeView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.OwnerElementId)).ToArray());
            Aspects = Array.AsReadOnly(snapshot.Aspects.Values.Select(value => new AspectView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.ElementId, value.ScopeId)).ToArray());
            Relations = Array.AsReadOnly(snapshot.Relations.Values.Select(value => new RelationView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray());
        }

        public Guid StateId { get; }
        public IReadOnlyCollection<ElementView> Elements { get; }
        public IReadOnlyCollection<ScopeView> Scopes { get; }
        public IReadOnlyCollection<AspectView> Aspects { get; }
        public IReadOnlyCollection<RelationView> Relations { get; }
    }
}
