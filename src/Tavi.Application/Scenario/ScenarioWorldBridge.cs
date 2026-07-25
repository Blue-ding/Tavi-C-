using Tavi.Domain.World;
using Tavi.Application.Extensions;
using ScenarioDomain = Tavi.Domain.Scenario;
using WorldDomain = Tavi.Domain.World;

namespace Tavi.Application.Scenario;

/// <summary>表示由 Scenario 相对来源 World 产生、尚未提交的结构化 World 提案。</summary>
public sealed record ScenarioWorldProposal
{
    /// <summary>获取提案要求仍然有效的来源 World StateId。</summary>
    public required Guid ExpectedWorldStateId { get; init; }
    /// <summary>获取将 Scenario 持久结果写入 World 所需的原子操作组。</summary>
    public required WorldChangeSet ChangeSet { get; init; }
}

/// <summary>在 World 和 Scenario 之间执行无 Module 行为的结构复制与差异编译。</summary>
public static class ScenarioWorldBridge
{
    /// <summary>把 World 的规则化 EARS 投影为不含 Scene 的初始 Scenario；Local 事实留在 World 中。</summary>
    public static ScenarioDomain.ScenarioSnapshot Import(WorldSnapshot world, ModuleCatalog catalog, IReadOnlyDictionary<Tavi.Extensibility.ModuleId, IReadOnlyDictionary<string, string>>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        return new ScenarioDomain.ScenarioSnapshot
        {
            SourceWorldStateId = world.Id,
            Modules = catalog.Modules.Select(module => new ScenarioDomain.ScenarioModuleReference(module.Id.Value, module.Version.Value, parameters?.GetValueOrDefault(module.Id))).ToList(),
            Elements = world.Elements.ToDictionary(pair => pair.Key, pair => new ScenarioDomain.Element(pair.Value.Id, pair.Value.Name, pair.Value.Description, new ScenarioDomain.ElementType(pair.Value.Type.Value))),
            Scopes = world.Scopes.ToDictionary(pair => pair.Key, pair => new ScenarioDomain.Scope(pair.Value.Id, pair.Value.Quantity, new ScenarioDomain.ScopeType(pair.Value.Type.Value), pair.Value.OwnerElementId)),
            Aspects = world.Aspects.ToDictionary(pair => pair.Key, pair => new ScenarioDomain.Aspect(pair.Value.Id, pair.Value.Quantity, new ScenarioDomain.AspectType(pair.Value.Type.Value), pair.Value.ElementId, pair.Value.ScopeId)),
            Relations = world.Relations.ToDictionary(pair => pair.Key, pair => new ScenarioDomain.Relation(pair.Value.Id, pair.Value.Quantity, new ScenarioDomain.RelationType(pair.Value.Type.Value), pair.Value.SourceElementId, pair.Value.TargetElementId, pair.Value.ScopeId))
        };
    }

    /// <summary>比较来源 World 与 Scenario 并生成可供玩家审阅的 World 提案；该方法不会提交 World。</summary>
    public static ScenarioWorldProposal CreateProposal(WorldSnapshot sourceWorld, ScenarioDomain.ScenarioSnapshot scenario)
    {
        ArgumentNullException.ThrowIfNull(sourceWorld);
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.SourceWorldStateId != sourceWorld.Id)
            throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(CreateProposal), $"Scenario 来源 WorldStateId {scenario.SourceWorldStateId} 与当前 World {sourceWorld.Id} 不一致。");
        var operations = new List<WorldOperation>();
        AppendRemovals(sourceWorld, scenario, operations);
        AppendElementChanges(sourceWorld, scenario, operations);
        AppendScopeChanges(sourceWorld, scenario, operations);
        AppendAspectChanges(sourceWorld, scenario, operations);
        AppendRelationChanges(sourceWorld, scenario, operations);
        return new ScenarioWorldProposal { ExpectedWorldStateId = sourceWorld.Id, ChangeSet = new WorldChangeSet(operations) };
    }

    private static void AppendRemovals(WorldSnapshot world, ScenarioDomain.ScenarioSnapshot scenario, List<WorldOperation> operations)
    {
        HashSet<Guid> removedElements = world.Elements.Keys.Except(scenario.Elements.Keys).ToHashSet();
        HashSet<Guid> cascadedScopes = world.Scopes.Values.Where(scope => removedElements.Contains(scope.OwnerElementId)).Select(scope => scope.Id).ToHashSet();
        foreach (Guid relationId in world.Relations.Values.Where(relation => !scenario.Relations.ContainsKey(relation.Id) && !removedElements.Contains(relation.SourceElementId) && !removedElements.Contains(relation.TargetElementId) && !cascadedScopes.Contains(relation.ScopeId)).Select(relation => relation.Id))
            operations.Add(new WorldDomain.RemoveRelationOperation(relationId));
        foreach (Guid aspectId in world.Aspects.Values.Where(aspect => !scenario.Aspects.ContainsKey(aspect.Id) && !removedElements.Contains(aspect.ElementId) && !cascadedScopes.Contains(aspect.ScopeId)).Select(aspect => aspect.Id))
            operations.Add(new WorldDomain.RemoveAspectOperation(aspectId));
        foreach (Guid scopeId in world.Scopes.Values.Where(scope => !scenario.Scopes.ContainsKey(scope.Id) && !cascadedScopes.Contains(scope.Id)).Select(scope => scope.Id))
            operations.Add(new WorldDomain.RemoveScopeOperation(scopeId));
        foreach (Guid elementId in removedElements)
            operations.Add(new WorldDomain.RemoveElementOperation(elementId));
    }

    private static void AppendElementChanges(WorldSnapshot world, ScenarioDomain.ScenarioSnapshot scenario, List<WorldOperation> operations)
    {
        foreach (ScenarioDomain.Element value in scenario.Elements.Values)
        {
            if (!world.Elements.TryGetValue(value.Id, out WorldDomain.Element? current))
            {
                operations.Add(new WorldDomain.AddElementOperation(value.Id, value.Name, value.Description, new WorldDomain.ElementType(value.Type.Value)));
                continue;
            }
            if (current.Name != value.Name)
                operations.Add(new UpdateElementNameOperation(value.Id, value.Name));
            if (current.Description != value.Description)
                operations.Add(new UpdateElementDescriptionOperation(value.Id, value.Description));
            if (current.Type.Value != value.Type.Value)
                operations.Add(new UpdateElementTypeOperation(value.Id, new WorldDomain.ElementType(value.Type.Value)));
        }
    }

    private static void AppendScopeChanges(WorldSnapshot world, ScenarioDomain.ScenarioSnapshot scenario, List<WorldOperation> operations)
    {
        foreach (ScenarioDomain.Scope value in scenario.Scopes.Values)
        {
            if (!world.Scopes.TryGetValue(value.Id, out WorldDomain.Scope? current))
            {
                operations.Add(new WorldDomain.AddScopeOperation(value.Id, value.Quantity, new WorldDomain.ScopeType(value.Type.Value), value.OwnerElementId));
                continue;
            }
            if (current.OwnerElementId != value.OwnerElementId)
                throw Structural(nameof(ScenarioDomain.Scope), value.Id);
            if (current.Quantity != value.Quantity)
                operations.Add(new UpdateScopeQuantityOperation(value.Id, value.Quantity));
            if (current.Type.Value != value.Type.Value)
                operations.Add(new UpdateScopeTypeOperation(value.Id, new WorldDomain.ScopeType(value.Type.Value)));
        }
    }

    private static void AppendAspectChanges(WorldSnapshot world, ScenarioDomain.ScenarioSnapshot scenario, List<WorldOperation> operations)
    {
        foreach (ScenarioDomain.Aspect value in scenario.Aspects.Values)
        {
            if (!world.Aspects.TryGetValue(value.Id, out WorldDomain.Aspect? current))
            {
                operations.Add(new WorldDomain.AddAspectOperation(value.Id, value.Quantity, new WorldDomain.AspectType(value.Type.Value), value.ElementId, value.ScopeId));
                continue;
            }
            if (current.ElementId != value.ElementId || current.ScopeId != value.ScopeId)
                throw Structural(nameof(ScenarioDomain.Aspect), value.Id);
            if (current.Quantity != value.Quantity)
                operations.Add(new UpdateAspectQuantityOperation(value.Id, value.Quantity));
            if (current.Type.Value != value.Type.Value)
                operations.Add(new UpdateAspectTypeOperation(value.Id, new WorldDomain.AspectType(value.Type.Value)));
        }
    }

    private static void AppendRelationChanges(WorldSnapshot world, ScenarioDomain.ScenarioSnapshot scenario, List<WorldOperation> operations)
    {
        foreach (ScenarioDomain.Relation value in scenario.Relations.Values)
        {
            if (!world.Relations.TryGetValue(value.Id, out WorldDomain.Relation? current))
            {
                operations.Add(new WorldDomain.AddRelationOperation(value.Id, value.Quantity, new WorldDomain.RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId));
                continue;
            }
            if (current.SourceElementId != value.SourceElementId || current.TargetElementId != value.TargetElementId || current.ScopeId != value.ScopeId)
                throw Structural(nameof(ScenarioDomain.Relation), value.Id);
            if (current.Quantity != value.Quantity)
                operations.Add(new UpdateRelationQuantityOperation(value.Id, value.Quantity));
            if (current.Type.Value != value.Type.Value)
                operations.Add(new UpdateRelationTypeOperation(value.Id, new WorldDomain.RelationType(value.Type.Value)));
        }
    }

    private static ScenarioApplicationException Structural(string entity, Guid id) => new(ScenarioApplicationErrorCodes.SemanticViolation, TaviErrorCategory.Validation, nameof(CreateProposal), $"{entity} {id} 的结构端点已变化；请使用删除并重建表达该变化。");
}
