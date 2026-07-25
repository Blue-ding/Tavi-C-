using Tavi.Extensibility;
using DomainScenario = Tavi.Domain.Scenario;

namespace Tavi.Application.Evolution;

internal static class ScenarioExtensibilityAdapter
{
    internal static IScenarioView ToView(DomainScenario.ScenarioSnapshot snapshot) => new SnapshotView(snapshot);

    internal static DomainScenario.ScenarioChangeSet ToChangeSet(ScenarioChangeProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return new DomainScenario.ScenarioChangeSet(proposal.Operations.Select(ToOperation));
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

    internal static ScenarioSceneView ToView(DomainScenario.Scene scene) => new(scene.Id, new SemanticKey(scene.DefinitionId.Value), new ModuleId(scene.ModuleId), new ModuleVersion(scene.ModuleVersion), scene.BasedOnScenarioStateId, scene.State.ToString(), Array.AsReadOnly(scene.GetBindings().Select(binding => new SceneSlotBindingView(binding.SlotId, Array.AsReadOnly(binding.ElementIds.ToArray()))).ToArray()));

    private static DomainScenario.ScenarioOperation ToOperation(ScenarioOperationIntent operation) => operation switch
    {
        ScenarioOperationIntent.AddElement value => new DomainScenario.AddElementOperation(value.Id, value.Name, value.Description, new DomainScenario.ElementType(value.Type.Value)),
        ScenarioOperationIntent.RemoveElement value => new DomainScenario.RemoveElementOperation(value.Id),
        ScenarioOperationIntent.UpdateElement value => new DomainScenario.UpdateElementOperation(value.Id, value.Name, value.Description, new DomainScenario.ElementType(value.Type.Value)),
        ScenarioOperationIntent.AddScope value => new DomainScenario.AddScopeOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.ScopeType(value.Type.Value), value.OwnerElementId),
        ScenarioOperationIntent.RemoveScope value => new DomainScenario.RemoveScopeOperation(value.Id),
        ScenarioOperationIntent.UpdateScope value => new DomainScenario.UpdateScopeOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.ScopeType(value.Type.Value)),
        ScenarioOperationIntent.AddAspect value => new DomainScenario.AddAspectOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.AspectType(value.Type.Value), value.ElementId, value.ScopeId),
        ScenarioOperationIntent.RemoveAspect value => new DomainScenario.RemoveAspectOperation(value.Id),
        ScenarioOperationIntent.UpdateAspect value => new DomainScenario.UpdateAspectOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.AspectType(value.Type.Value)),
        ScenarioOperationIntent.AddRelation value => new DomainScenario.AddRelationOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId),
        ScenarioOperationIntent.RemoveRelation value => new DomainScenario.RemoveRelationOperation(value.Id),
        ScenarioOperationIntent.UpdateRelation value => new DomainScenario.UpdateRelationOperation(value.Id, value.Name, value.Description, value.Quantity, new DomainScenario.RelationType(value.Type.Value)),
        _ => throw new EvolutionException(EvolutionErrorCodes.SemanticViolation, TaviErrorCategory.Protocol, nameof(ToChangeSet), $"不支持的 ScenarioOperationIntent 类型 {operation.GetType().FullName}。")
    };

    private sealed class SnapshotView : IScenarioView
    {
        internal SnapshotView(DomainScenario.ScenarioSnapshot snapshot)
        {
            StateId = snapshot.Id;
            SourceWorldStateId = snapshot.SourceWorldStateId;
            Elements = Array.AsReadOnly(snapshot.Elements.Values.Select(value => new ScenarioElementView(value.Id, value.Name, value.Description, new SemanticKey(value.Type.Value))).ToArray());
            Aspects = Array.AsReadOnly(snapshot.Aspects.Values.Select(value => new ScenarioAspectView(value.Id, value.Name, value.Description, value.Quantity, new SemanticKey(value.Type.Value), value.ElementId, value.ScopeId)).ToArray());
            Relations = Array.AsReadOnly(snapshot.Relations.Values.Select(value => new ScenarioRelationView(value.Id, value.Name, value.Description, value.Quantity, new SemanticKey(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray());
            Scopes = Array.AsReadOnly(snapshot.Scopes.Values.Select(value => new ScenarioScopeView(value.Id, value.Name, value.Description, value.Quantity, new SemanticKey(value.Type.Value), value.OwnerElementId)).ToArray());
            Scenes = Array.AsReadOnly(snapshot.Scenes.Values.Select(ToView).ToArray());
        }

        public Guid StateId { get; }
        public Guid SourceWorldStateId { get; }
        public IReadOnlyCollection<ScenarioElementView> Elements { get; }
        public IReadOnlyCollection<ScenarioAspectView> Aspects { get; }
        public IReadOnlyCollection<ScenarioRelationView> Relations { get; }
        public IReadOnlyCollection<ScenarioScopeView> Scopes { get; }
        public IReadOnlyCollection<ScenarioSceneView> Scenes { get; }
    }
}
