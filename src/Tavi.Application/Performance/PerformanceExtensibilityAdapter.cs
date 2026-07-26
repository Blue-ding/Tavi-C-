using Tavi.Application.Extensions;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using RuntimePerformance = Tavi.Domain.Performance.Performance;

namespace Tavi.Application.Performance;

internal static class PerformanceExtensibilityAdapter
{
    internal static PerformanceSnapshot CreateSeed(SceneContextView context, FrozenModuleRuntime extensions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extensions);
        ModuleId module = context.Scene.Module;
        IReadOnlyDictionary<string, string> parameters = extensions.GetParameters(module);
        return new PerformanceSnapshot
        {
            SourceScenarioStateId = context.ScenarioStateId,
            SourceScene = new PerformanceSourceScene
            {
                Id = context.Scene.Id,
                DefinitionId = context.Scene.DefinitionId.Value,
                ModuleId = context.Scene.Module.Value,
                ModuleVersion = context.Scene.ModuleVersion.Value,
                BasedOnScenarioStateId = context.Scene.BasedOnScenarioStateId,
                State = context.Scene.State,
                Bindings = context.Scene.Bindings.Select(value => new PerformanceSourceSceneBinding(value.SlotId, value.ElementIds)).ToArray(),
                Elements = context.Elements.Select(value => new Element(value.Id, value.Name, value.Description, new ElementType(value.Type.Value))).ToArray(),
                Scopes = context.Scopes.Select(value => new Scope(value.Id, value.Quantity, new ScopeType(value.Type.Value), value.OwnerElementId)).ToArray(),
                Aspects = context.Aspects.Select(value => new Aspect(value.Id, value.Quantity, new AspectType(value.Type.Value), value.ElementId, value.ScopeId)).ToArray(),
                Relations = context.Relations.Select(value => new Relation(value.Id, value.Quantity, new RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray()
            },
            Modules = [new PerformanceModuleReference(module.Value, context.Scene.ModuleVersion.Value, parameters)],
            ImportedElementIds = context.Elements.Select(value => value.Id).ToHashSet(),
            Elements = context.Elements.ToDictionary(value => value.Id, value => new Element(value.Id, value.Name, value.Description, new ElementType(value.Type.Value))),
            Scopes = context.Scopes.ToDictionary(value => value.Id, value => new Scope(value.Id, value.Quantity, new ScopeType(value.Type.Value), value.OwnerElementId)),
            Aspects = context.Aspects.ToDictionary(value => value.Id, value => new Aspect(value.Id, value.Quantity, new AspectType(value.Type.Value), value.ElementId, value.ScopeId)),
            Relations = context.Relations.ToDictionary(value => value.Id, value => new Relation(value.Id, value.Quantity, new RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId))
        };
    }

    internal static PerformanceChangeSet ToChangeSet(IEnumerable<PerformanceOperationIntent> intents)
        => new(intents.Select(ToOperation));

    internal static PerformanceOperation ToOperation(PerformanceOperationIntent intent) => intent switch
    {
        PerformanceOperationIntent.AddElement value => new AddPerformanceElementOperation(value.Id, value.Name, value.Description, new ElementType(value.Type.Value)),
        PerformanceOperationIntent.RemoveElement value => new RemovePerformanceElementOperation(value.Id),
        PerformanceOperationIntent.UpdateElement value => new UpdatePerformanceElementOperation(value.Id, value.Name, value.Description, new ElementType(value.Type.Value)),
        PerformanceOperationIntent.AddScope value => new AddPerformanceScopeOperation(value.Id, value.Quantity, new ScopeType(value.Type.Value), value.OwnerElementId),
        PerformanceOperationIntent.RemoveScope value => new RemovePerformanceScopeOperation(value.Id),
        PerformanceOperationIntent.UpdateScope value => new UpdatePerformanceScopeOperation(value.Id, value.Quantity, new ScopeType(value.Type.Value)),
        PerformanceOperationIntent.AddAspect value => new AddPerformanceAspectOperation(value.Id, value.Quantity, new AspectType(value.Type.Value), value.ElementId, value.ScopeId),
        PerformanceOperationIntent.RemoveAspect value => new RemovePerformanceAspectOperation(value.Id),
        PerformanceOperationIntent.UpdateAspect value => new UpdatePerformanceAspectOperation(value.Id, value.Quantity, new AspectType(value.Type.Value)),
        PerformanceOperationIntent.AddRelation value => new AddPerformanceRelationOperation(value.Id, value.Quantity, new RelationType(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId),
        PerformanceOperationIntent.RemoveRelation value => new RemovePerformanceRelationOperation(value.Id),
        PerformanceOperationIntent.UpdateRelation value => new UpdatePerformanceRelationOperation(value.Id, value.Quantity, new RelationType(value.Type.Value)),
        _ => throw new ArgumentException($"不支持的 PerformanceOperationIntent {intent.GetType().Name}。", nameof(intent))
    };

    internal static IPerformanceView ToView(RuntimePerformance performance) => new SnapshotView(performance.CreateSnapshot());
    internal static PerformanceBeatView ToView(Beat beat) => new(
        beat.Id,
        new SemanticKey(beat.DefinitionId.Value),
        new ModuleId(beat.ModuleId),
        new ModuleVersion(beat.ModuleVersion),
        beat.BasedOnPerformanceStateId,
        beat.State.ToString(),
        beat.GetBindings().Select(value => new BeatSlotBindingView(value.SlotId, Array.AsReadOnly(value.ElementIds.ToArray()))).ToArray());

    private sealed class SnapshotView : IPerformanceView
    {
        internal SnapshotView(PerformanceSnapshot snapshot)
        {
            Id = snapshot.PerformanceId;
            StateId = snapshot.StateId;
            SourceScenarioStateId = snapshot.SourceScenarioStateId;
            SourceSceneId = snapshot.SourceScene.Id;
            Status = snapshot.Status.ToString();
            Elements = snapshot.Elements.Values.Select(value => new ElementView(value.Id, value.Name, value.Description, new SemanticKey(value.Type.Value))).ToArray();
            Scopes = snapshot.Scopes.Values.Select(value => new ScopeView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.OwnerElementId)).ToArray();
            Aspects = snapshot.Aspects.Values.Select(value => new AspectView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.ElementId, value.ScopeId)).ToArray();
            Relations = snapshot.Relations.Values.Select(value => new RelationView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray();
            Beats = snapshot.Beats.Values.Select(PerformanceExtensibilityAdapter.ToView).ToArray();
        }

        public Guid Id { get; }
        public Guid StateId { get; }
        public Guid SourceScenarioStateId { get; }
        public Guid SourceSceneId { get; }
        public string Status { get; }
        public IReadOnlyCollection<ElementView> Elements { get; }
        public IReadOnlyCollection<ScopeView> Scopes { get; }
        public IReadOnlyCollection<AspectView> Aspects { get; }
        public IReadOnlyCollection<RelationView> Relations { get; }
        public IReadOnlyCollection<PerformanceBeatView> Beats { get; }
    }

    internal static SceneContextView ToSourceSceneContext(PerformanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PerformanceSourceScene scene = snapshot.SourceScene;
        return new SceneContextView
        {
            ScenarioStateId = snapshot.SourceScenarioStateId,
            Scene = new ScenarioSceneView(
                scene.Id,
                new SemanticKey(scene.DefinitionId),
                new ModuleId(scene.ModuleId),
                new ModuleVersion(scene.ModuleVersion),
                scene.BasedOnScenarioStateId,
                scene.State,
                scene.Bindings.Select(value => new SceneSlotBindingView(value.SlotId, value.ElementIds)).ToArray()),
            Elements = scene.Elements.Select(value => new ElementView(value.Id, value.Name, value.Description, new SemanticKey(value.Type.Value))).ToArray(),
            Scopes = scene.Scopes.Select(value => new ScopeView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.OwnerElementId)).ToArray(),
            Aspects = scene.Aspects.Select(value => new AspectView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.ElementId, value.ScopeId)).ToArray(),
            Relations = scene.Relations.Select(value => new RelationView(value.Id, value.Quantity, new SemanticKey(value.Type.Value), value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray()
        };
    }
}
