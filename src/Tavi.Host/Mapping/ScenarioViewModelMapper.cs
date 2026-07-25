using Tavi.Application.Scenario;
using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Mapping;

/// <summary>把 Scenario 快照与 Definition 转换为不泄漏运行时引用的 Host 契约。</summary>
internal static class ScenarioViewModelMapper
{
    internal static ScenarioWorkspaceViewModel ToWorkspace(IScenarioService service, IReadOnlyList<SceneDefinition> definitions)
    {
        ScenarioSnapshot snapshot = service.Queries.CreateSnapshot();
        ElementViewModel[] elements = snapshot.Elements.Values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).Select(value => new ElementViewModel(value.Id, value.Name, value.Description, value.Type.Value)).ToArray();
        AspectViewModel[] aspects = snapshot.Aspects.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new AspectViewModel(value.Id, value.Quantity, value.Type.Value, value.ElementId, value.ScopeId)).ToArray();
        RelationViewModel[] relations = snapshot.Relations.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new RelationViewModel(value.Id, value.Quantity, value.Type.Value, value.SourceElementId, value.TargetElementId, value.ScopeId)).ToArray();
        ScopeViewModel[] scopes = snapshot.Scopes.Values.OrderBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id).Select(value => new ScopeViewModel(value.Id, value.Quantity, value.Type.Value, value.OwnerElementId)).ToArray();
        ScenarioModuleViewModel[] modules = snapshot.Modules.OrderBy(value => value.Id, StringComparer.Ordinal).Select(value => new ScenarioModuleViewModel(value.Id, value.Version)).ToArray();
        SceneDefinitionViewModel[] mappedDefinitions = definitions.Select(ToDefinition).ToArray();
        SceneViewModel[] scenes = snapshot.Scenes.Values.OrderBy(value => value.State).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).Select(ToScene).ToArray();
        return new ScenarioWorkspaceViewModel(snapshot.Id, snapshot.SourceWorldStateId, service.IsDirty, service.CanUndo, service.CanRedo, service.Health.ToString(), modules, elements, aspects, relations, scopes, mappedDefinitions, scenes);
    }

    private static SceneDefinitionViewModel ToDefinition(SceneDefinition definition) => new(definition.Id.Value, definition.Module.Value, definition.ModuleVersion.Value, definition.Name, definition.Description, Settlement(definition.SettlementCapabilities), definition.Slots.Select(slot => new SceneSlotViewModel(slot.Id, slot.Name, slot.Description, slot.Minimum, slot.Maximum, slot.Requirement.ElementTypes.Select(value => value.Value).Order(StringComparer.Ordinal).ToArray(), slot.Requirement.RequiredAspectGroups.Select(value => value.Value).Order(StringComparer.Ordinal).ToArray(), [])).ToArray());

    private static SceneViewModel ToScene(Scene scene)
    {
        Dictionary<string, IReadOnlyList<Guid>> bindings = scene.GetBindings().ToDictionary(value => value.SlotId, value => value.ElementIds, StringComparer.Ordinal);
        SceneSlotViewModel[] slots = scene.GetSlotSpecifications().Select(slot => new SceneSlotViewModel(slot.Id, slot.Name, slot.Description, slot.Minimum, slot.Maximum, slot.ElementTypes, slot.RequiredAspectGroups, bindings.GetValueOrDefault(slot.Id) ?? [])).ToArray();
        return new SceneViewModel(scene.Id, scene.DefinitionId.Value, scene.ModuleId, scene.ModuleVersion, scene.Name, scene.Description, scene.State.ToString(), Settlement(scene.SettlementOptions), scene.DefinitionFrozen, slots);
    }

    private static IReadOnlyList<string> Settlement(SceneSettlementCapabilities capabilities) => Enum.GetValues<SceneSettlementCapabilities>().Where(value => value != SceneSettlementCapabilities.None && capabilities.HasFlag(value)).Select(value => value.ToString()).ToArray();
    private static IReadOnlyList<string> Settlement(SceneSettlementOptions options) => Enum.GetValues<SceneSettlementOptions>().Where(value => value != SceneSettlementOptions.None && options.HasFlag(value)).Select(value => value.ToString()).ToArray();
}
