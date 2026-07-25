using Tavi.Extensibility;
using Tavi.Application.Extensions;
using DomainScenario = Tavi.Domain.Scenario;

namespace Tavi.Application.Scenario;

/// <summary>在公开 Module 契约和内部 Scenario Domain 之间复制、收窄并校验数据。</summary>
internal static class ScenarioExtensibilityAdapter
{
    internal static IScenarioView ToView(DomainScenario.ScenarioSnapshot snapshot) => new SnapshotView(snapshot);

    internal static SceneContextView ToSceneContext(DomainScenario.ScenarioSnapshot snapshot, DomainScenario.Scene scene)
    {
        HashSet<Guid> elementIds = scene.GetBindings().SelectMany(binding => binding.ElementIds).Where(snapshot.Elements.ContainsKey).ToHashSet();
        DomainScenario.Scope[] scopes = snapshot.Scopes.Values.Where(scope => elementIds.Contains(scope.OwnerElementId)).ToArray();
        HashSet<Guid> scopeIds = scopes.Select(scope => scope.Id).ToHashSet();
        return new SceneContextView
        {
            ScenarioStateId = snapshot.Id,
            Scene = ToView(scene),
            Elements = Array.AsReadOnly(snapshot.Elements.Values.Where(element => elementIds.Contains(element.Id)).Select(ToView).ToArray()),
            Scopes = Array.AsReadOnly(scopes.Select(ToView).ToArray()),
            Aspects = Array.AsReadOnly(snapshot.Aspects.Values.Where(aspect => elementIds.Contains(aspect.ElementId) && scopeIds.Contains(aspect.ScopeId)).Select(ToView).ToArray()),
            Relations = Array.AsReadOnly(snapshot.Relations.Values.Where(relation => elementIds.Contains(relation.SourceElementId) && elementIds.Contains(relation.TargetElementId) && scopeIds.Contains(relation.ScopeId)).Select(ToView).ToArray())
        };
    }

    internal static DomainScenario.SceneSettlementOptions ToDomain(SceneSettlementCapabilities value)
    {
        DomainScenario.SceneSettlementOptions result = DomainScenario.SceneSettlementOptions.None;
        if (value.HasFlag(SceneSettlementCapabilities.Rules))
            result |= DomainScenario.SceneSettlementOptions.Rules;
        if (value.HasFlag(SceneSettlementCapabilities.Writing))
            result |= DomainScenario.SceneSettlementOptions.Writing;
        return result;
    }

    internal static IReadOnlyList<DomainScenario.SceneSlotSpecification> ToDomainSlots(SceneDefinition definition) => definition.Slots.Select(slot => new DomainScenario.SceneSlotSpecification(slot.Id, slot.Name, slot.Description, slot.Minimum, slot.Maximum, slot.Requirement.ElementTypes.Select(value => value.Value), slot.Requirement.RequiredAspectGroups.Select(value => value.Value))).ToArray();

    internal static SceneDefinition ToDefinition(DomainScenario.Scene scene) => new()
    {
        Id = new SemanticKey(scene.DefinitionId.Value),
        Module = new ModuleId(scene.ModuleId),
        ModuleVersion = new ModuleVersion(scene.ModuleVersion),
        Name = scene.Name,
        Description = scene.Description,
        SettlementCapabilities = ToExtensibility(scene.SettlementOptions),
        SourceScenarioStateId = scene.BasedOnScenarioStateId,
        Slots = scene.GetSlotSpecifications().Select(slot => new SceneSlotDefinition
        {
            Id = slot.Id,
            Name = slot.Name,
            Description = slot.Description,
            Minimum = slot.Minimum,
            Maximum = slot.Maximum,
            Requirement = new SceneSlotRequirement { ElementTypes = slot.ElementTypes.Select(value => new SemanticKey(value)).ToHashSet(), RequiredAspectGroups = slot.RequiredAspectGroups.Select(value => new SemanticKey(value)).ToHashSet() }
        }).ToArray()
    };

    internal static DomainScenario.ScenarioChangeSet ToLocalChangeSet(SceneSettlementProposal proposal, DomainScenario.ScenarioSnapshot snapshot, DomainScenario.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(scene);
        var boundary = new LocalBoundary(snapshot, scene);
        var operations = new List<DomainScenario.ScenarioOperation>();
        foreach (SceneOperationIntent operation in proposal.Operations)
            operations.Add(boundary.Convert(operation));
        return new DomainScenario.ScenarioChangeSet(operations);
    }

    internal static ScenarioSceneView ToView(DomainScenario.Scene scene) => new(scene.Id, new SemanticKey(scene.DefinitionId.Value), new ModuleId(scene.ModuleId), new ModuleVersion(scene.ModuleVersion), scene.BasedOnScenarioStateId, scene.State.ToString(), Array.AsReadOnly(scene.GetBindings().Select(binding => new SceneSlotBindingView(binding.SlotId, Array.AsReadOnly(binding.ElementIds.ToArray()))).ToArray()));

    private static SceneSettlementCapabilities ToExtensibility(DomainScenario.SceneSettlementOptions value)
    {
        SceneSettlementCapabilities result = SceneSettlementCapabilities.None;
        if (value.HasFlag(DomainScenario.SceneSettlementOptions.Rules))
            result |= SceneSettlementCapabilities.Rules;
        if (value.HasFlag(DomainScenario.SceneSettlementOptions.Writing))
            result |= SceneSettlementCapabilities.Writing;
        return result;
    }

    private static ElementView ToView(DomainScenario.Element value) => new(value.Id, value.Name, value.Description, new SemanticKey(value.Type.Value));
    private static ScopeView ToView(DomainScenario.Scope value) => new(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.OwnerElementId);
    private static AspectView ToView(DomainScenario.Aspect value) => new(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.ElementId, value.ScopeId);
    private static RelationView ToView(DomainScenario.Relation value) => new(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId);

    /// <summary>逐项模拟局部实体集合，确保新建实体可被后续操作引用而既有外部实体永远不可寻址。</summary>
    private sealed class LocalBoundary
    {
        private readonly DomainScenario.ScenarioSnapshot _snapshot;
        private readonly HashSet<Guid> _elements;
        private readonly HashSet<Guid> _scopes;
        private readonly HashSet<Guid> _aspects;
        private readonly HashSet<Guid> _relations;
        private readonly Dictionary<Guid, Guid> _scopeOwners;
        private readonly Dictionary<Guid, (Guid ElementId, Guid ScopeId)> _aspectReferences;
        private readonly Dictionary<Guid, (Guid SourceId, Guid TargetId, Guid ScopeId)> _relationReferences;

        internal LocalBoundary(DomainScenario.ScenarioSnapshot snapshot, DomainScenario.Scene scene)
        {
            _snapshot = snapshot;
            _elements = scene.GetBindings().SelectMany(binding => binding.ElementIds).ToHashSet();
            _scopes = snapshot.Scopes.Values.Where(scope => _elements.Contains(scope.OwnerElementId)).Select(scope => scope.Id).ToHashSet();
            _aspects = snapshot.Aspects.Values.Where(aspect => _elements.Contains(aspect.ElementId) && _scopes.Contains(aspect.ScopeId)).Select(aspect => aspect.Id).ToHashSet();
            _relations = snapshot.Relations.Values.Where(relation => _elements.Contains(relation.SourceElementId) && _elements.Contains(relation.TargetElementId) && _scopes.Contains(relation.ScopeId)).Select(relation => relation.Id).ToHashSet();
            _scopeOwners = snapshot.Scopes.Values.Where(scope => _scopes.Contains(scope.Id)).ToDictionary(scope => scope.Id, scope => scope.OwnerElementId);
            _aspectReferences = snapshot.Aspects.Values.Where(aspect => _aspects.Contains(aspect.Id)).ToDictionary(aspect => aspect.Id, aspect => (aspect.ElementId, aspect.ScopeId));
            _relationReferences = snapshot.Relations.Values.Where(relation => _relations.Contains(relation.Id)).ToDictionary(relation => relation.Id, relation => (relation.SourceElementId, relation.TargetElementId, relation.ScopeId));
        }

        internal DomainScenario.ScenarioOperation Convert(SceneOperationIntent operation) => operation switch
        {
            SceneOperationIntent.AddElement value => AddElement(value),
            SceneOperationIntent.RemoveElement value => RemoveElement(value),
            SceneOperationIntent.UpdateElement value => RequireElement(value.Id, new DomainScenario.UpdateElementOperation(value.Id, value.Name, value.Description, new DomainScenario.ElementType(value.Type.Value))),
            SceneOperationIntent.AddScope value => AddScope(value),
            SceneOperationIntent.RemoveScope value => RemoveScope(value),
            SceneOperationIntent.UpdateScope value => RequireScope(value.Id, new DomainScenario.UpdateScopeOperation(value.Id, value.Quantity, new DomainScenario.ScopeType(value.Type.Value))),
            SceneOperationIntent.AddAspect value => AddAspect(value),
            SceneOperationIntent.RemoveAspect value => RemoveAspect(value),
            SceneOperationIntent.UpdateAspect value => RequireAspect(value.Id, new DomainScenario.UpdateAspectOperation(value.Id, value.Quantity, new DomainScenario.AspectType(value.Type.Value))),
            SceneOperationIntent.AddRelation value => AddRelation(value),
            SceneOperationIntent.RemoveRelation value => RemoveRelation(value),
            SceneOperationIntent.UpdateRelation value => RequireRelation(value.Id, new DomainScenario.UpdateRelationOperation(value.Id, value.Quantity, new DomainScenario.RelationType(value.Type.Value))),
            _ => throw Invalid($"不支持的 SceneOperationIntent 类型 {operation.GetType().FullName}。")
        };

        private DomainScenario.ScenarioOperation AddElement(SceneOperationIntent.AddElement value)
        {
            EnsureNew(value.Id, _snapshot.Elements.ContainsKey(value.Id) || !_elements.Add(value.Id), "Element");
            return new DomainScenario.AddElementOperation(value.Id, value.Name, value.Description, new DomainScenario.ElementType(value.Type.Value));
        }

        private DomainScenario.ScenarioOperation RemoveElement(SceneOperationIntent.RemoveElement value)
        {
            Require(_elements.Remove(value.Id), "Element", value.Id);
            Guid[] ownedScopes = _scopeOwners.Where(pair => pair.Value == value.Id).Select(pair => pair.Key).ToArray();
            _scopes.ExceptWith(ownedScopes);
            foreach (Guid scopeId in ownedScopes)
                _scopeOwners.Remove(scopeId);
            Guid[] removedAspects = _aspectReferences.Where(pair => pair.Value.ElementId == value.Id || ownedScopes.Contains(pair.Value.ScopeId)).Select(pair => pair.Key).ToArray();
            Guid[] removedRelations = _relationReferences.Where(pair => pair.Value.SourceId == value.Id || pair.Value.TargetId == value.Id || ownedScopes.Contains(pair.Value.ScopeId)).Select(pair => pair.Key).ToArray();
            _aspects.ExceptWith(removedAspects);
            _relations.ExceptWith(removedRelations);
            foreach (Guid id in removedAspects)
                _aspectReferences.Remove(id);
            foreach (Guid id in removedRelations)
                _relationReferences.Remove(id);
            return new DomainScenario.RemoveElementOperation(value.Id);
        }

        private DomainScenario.ScenarioOperation AddScope(SceneOperationIntent.AddScope value)
        {
            Require(_elements.Contains(value.OwnerElementId), "Element", value.OwnerElementId);
            EnsureNew(value.Id, _snapshot.Scopes.ContainsKey(value.Id) || !_scopes.Add(value.Id), "Scope");
            _scopeOwners.Add(value.Id, value.OwnerElementId);
            return new DomainScenario.AddScopeOperation(value.Id, value.Quantity, new DomainScenario.ScopeType(value.Type.Value), value.OwnerElementId);
        }

        private DomainScenario.ScenarioOperation RemoveScope(SceneOperationIntent.RemoveScope value)
        {
            Require(_scopes.Remove(value.Id), "Scope", value.Id);
            _scopeOwners.Remove(value.Id);
            Guid[] removedAspects = _aspectReferences.Where(pair => pair.Value.ScopeId == value.Id).Select(pair => pair.Key).ToArray();
            Guid[] removedRelations = _relationReferences.Where(pair => pair.Value.ScopeId == value.Id).Select(pair => pair.Key).ToArray();
            _aspects.ExceptWith(removedAspects);
            _relations.ExceptWith(removedRelations);
            foreach (Guid id in removedAspects)
                _aspectReferences.Remove(id);
            foreach (Guid id in removedRelations)
                _relationReferences.Remove(id);
            return new DomainScenario.RemoveScopeOperation(value.Id);
        }

        private DomainScenario.ScenarioOperation AddAspect(SceneOperationIntent.AddAspect value)
        {
            Require(_elements.Contains(value.ElementId), "Element", value.ElementId);
            Require(_scopes.Contains(value.ScopeId), "Scope", value.ScopeId);
            EnsureNew(value.Id, _snapshot.Aspects.ContainsKey(value.Id) || !_aspects.Add(value.Id), "Aspect");
            _aspectReferences.Add(value.Id, (value.ElementId, value.ScopeId));
            return new DomainScenario.AddAspectOperation(value.Id, value.Quantity, new DomainScenario.AspectType(value.Type.Value), value.ElementId, value.ScopeId);
        }

        private DomainScenario.ScenarioOperation RemoveAspect(SceneOperationIntent.RemoveAspect value)
        {
            Require(_aspects.Remove(value.Id), "Aspect", value.Id);
            _aspectReferences.Remove(value.Id);
            return new DomainScenario.RemoveAspectOperation(value.Id);
        }

        private DomainScenario.ScenarioOperation AddRelation(SceneOperationIntent.AddRelation value)
        {
            Require(_elements.Contains(value.SourceElementId), "Element", value.SourceElementId);
            Require(_elements.Contains(value.TargetElementId), "Element", value.TargetElementId);
            Require(_scopes.Contains(value.ScopeId), "Scope", value.ScopeId);
            EnsureNew(value.Id, _snapshot.Relations.ContainsKey(value.Id) || !_relations.Add(value.Id), "Relation");
            _relationReferences.Add(value.Id, (value.SourceElementId, value.TargetElementId, value.ScopeId));
            return new DomainScenario.AddRelationOperation(value.Id, value.Quantity, new DomainScenario.RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId);
        }

        private DomainScenario.ScenarioOperation RemoveRelation(SceneOperationIntent.RemoveRelation value)
        {
            Require(_relations.Remove(value.Id), "Relation", value.Id);
            _relationReferences.Remove(value.Id);
            return new DomainScenario.RemoveRelationOperation(value.Id);
        }

        private DomainScenario.ScenarioOperation RequireElement(Guid id, DomainScenario.ScenarioOperation operation) => RequireOperation(_elements.Contains(id), "Element", id, operation);
        private DomainScenario.ScenarioOperation RequireScope(Guid id, DomainScenario.ScenarioOperation operation) => RequireOperation(_scopes.Contains(id), "Scope", id, operation);
        private DomainScenario.ScenarioOperation RequireAspect(Guid id, DomainScenario.ScenarioOperation operation) => RequireOperation(_aspects.Contains(id), "Aspect", id, operation);
        private DomainScenario.ScenarioOperation RequireRelation(Guid id, DomainScenario.ScenarioOperation operation) => RequireOperation(_relations.Contains(id), "Relation", id, operation);

        private static DomainScenario.ScenarioOperation RequireOperation(bool condition, string entity, Guid id, DomainScenario.ScenarioOperation operation)
        {
            Require(condition, entity, id);
            return operation;
        }

        private static void Require(bool condition, string entity, Guid id)
        {
            if (!condition)
                throw Invalid($"{entity} {id} 不属于当前 Scene 的局部结算边界。");
        }

        private static void EnsureNew(Guid id, bool existsGlobally, string entity)
        {
            if (id == Guid.Empty || existsGlobally)
                throw Invalid($"新建 {entity} 的标识 {id} 为空或已被 Scenario 使用。");
        }
    }

    private sealed class SnapshotView : IScenarioView
    {
        internal SnapshotView(DomainScenario.ScenarioSnapshot snapshot)
        {
            StateId = snapshot.Id;
            SourceWorldStateId = snapshot.SourceWorldStateId;
            Elements = Array.AsReadOnly(snapshot.Elements.Values.Select(ToView).ToArray());
            Aspects = Array.AsReadOnly(snapshot.Aspects.Values.Select(ToView).ToArray());
            Relations = Array.AsReadOnly(snapshot.Relations.Values.Select(ToView).ToArray());
            Scopes = Array.AsReadOnly(snapshot.Scopes.Values.Select(ToView).ToArray());
            Scenes = Array.AsReadOnly(snapshot.Scenes.Values.Select(ToView).ToArray());
        }

        public Guid StateId { get; }
        public Guid SourceWorldStateId { get; }
        public IReadOnlyCollection<ElementView> Elements { get; }
        public IReadOnlyCollection<AspectView> Aspects { get; }
        public IReadOnlyCollection<RelationView> Relations { get; }
        public IReadOnlyCollection<ScopeView> Scopes { get; }
        public IReadOnlyCollection<ScenarioSceneView> Scenes { get; }
    }

    private static ScenarioApplicationException Invalid(string message) => new(ScenarioApplicationErrorCodes.SemanticViolation, TaviErrorCategory.Protocol, nameof(ToLocalChangeSet), message);
}
