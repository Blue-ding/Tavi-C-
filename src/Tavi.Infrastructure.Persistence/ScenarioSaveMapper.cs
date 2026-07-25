using Tavi.Domain.Scenario;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Infrastructure.Persistence;

/// <summary>在 ScenarioSnapshot 与版本化 JSON DTO 之间执行无语义猜测的双向映射。</summary>
internal static class ScenarioSaveMapper
{
    internal static ScenarioSaveDataV1 FromDomain(ScenarioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ScenarioSaveDataV1
        {
            Id = snapshot.Id,
            SourceWorldStateId = snapshot.SourceWorldStateId,
            Modules = snapshot.Modules.Select(value => new ScenarioModuleSaveDataV1 { Id = value.Id, Version = value.Version, Parameters = new Dictionary<string, string>(value.Parameters, StringComparer.Ordinal) }).ToList(),
            Elements = snapshot.Elements.Values.Select(value => new ScenarioElementSaveDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value }).ToList(),
            Scopes = snapshot.Scopes.Values.Select(value => new ScenarioScopeSaveDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToList(),
            Aspects = snapshot.Aspects.Values.Select(value => new ScenarioAspectSaveDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToList(),
            Relations = snapshot.Relations.Values.Select(value => new ScenarioRelationSaveDataV1 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToList(),
            Scenes = snapshot.Scenes.Values.Select(value => new ScenarioSceneSaveDataV1
            {
                Id = value.Id,
                DefinitionId = value.DefinitionId.Value,
                ModuleId = value.ModuleId,
                ModuleVersion = value.ModuleVersion,
                BasedOnScenarioStateId = value.BasedOnScenarioStateId,
                Name = value.Name,
                Description = value.Description,
                SettlementOptions = (int)value.SettlementOptions,
                State = value.State.ToString(),
                DefinitionFrozen = value.DefinitionFrozen,
                Bindings = value.GetBindings().Select(binding => new ScenarioSceneBindingSaveDataV1 { SlotId = binding.SlotId, ElementIds = binding.ElementIds.ToList() }).ToList(),
                Slots = value.GetSlotSpecifications().Select(slot => new ScenarioSceneSlotSaveDataV1 { Id = slot.Id, Name = slot.Name, Description = slot.Description, Minimum = slot.Minimum, Maximum = slot.Maximum, ElementTypes = slot.ElementTypes.ToList(), RequiredAspectGroups = slot.RequiredAspectGroups.ToList() }).ToList()
            }).ToList()
        };
    }

    internal static ScenarioSnapshot ToDomain(ScenarioSaveDataV1 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Modules is null || data.Elements is null || data.Scopes is null || data.Aspects is null || data.Relations is null || data.Scenes is null)
            throw new InvalidDataException("Scenario V1 存档集合不能为 null。");
        var snapshot = new ScenarioSnapshot { Id = data.Id, SourceWorldStateId = data.SourceWorldStateId };
        foreach (ScenarioModuleSaveDataV1 value in data.Modules)
        {
            if (value is null)
                throw new InvalidDataException("Module 存档项不能为 null。");
            snapshot.Modules.Add(new ScenarioModuleReference(RequireText(value.Id, "Module.Id"), RequireText(value.Version, "Module.Version"), value.Parameters ?? new Dictionary<string, string>()));
        }
        foreach (ScenarioElementSaveDataV1 value in data.Elements)
        {
            RequireItem(value, "Element");
            Add(snapshot.Elements, value.Id, new Element(value.Id, RequireText(value.Name, "Element.Name"), RequireValue(value.Description, "Element.Description"), Parse(value.Type, "Element.Type", text => new ElementType(text))), "Element");
        }
        foreach (ScenarioScopeSaveDataV1 value in data.Scopes)
        {
            RequireItem(value, "Scope");
            Add(snapshot.Scopes, value.Id, new Scope(value.Id, RequireText(value.Name, "Scope.Name"), RequireValue(value.Description, "Scope.Description"), RequireFinite(value.Quantity, "Scope.Quantity"), Parse(value.Type, "Scope.Type", text => new ScopeType(text)), value.OwnerElementId), "Scope");
        }
        foreach (ScenarioAspectSaveDataV1 value in data.Aspects)
        {
            RequireItem(value, "Aspect");
            Add(snapshot.Aspects, value.Id, new Aspect(value.Id, RequireText(value.Name, "Aspect.Name"), RequireValue(value.Description, "Aspect.Description"), RequireFinite(value.Quantity, "Aspect.Quantity"), Parse(value.Type, "Aspect.Type", text => new AspectType(text)), value.ElementId, value.ScopeId), "Aspect");
        }
        foreach (ScenarioRelationSaveDataV1 value in data.Relations)
        {
            RequireItem(value, "Relation");
            Add(snapshot.Relations, value.Id, new Relation(value.Id, RequireText(value.Name, "Relation.Name"), RequireValue(value.Description, "Relation.Description"), RequireFinite(value.Quantity, "Relation.Quantity"), Parse(value.Type, "Relation.Type", text => new RelationType(text)), value.SourceElementId, value.TargetElementId, value.ScopeId), "Relation");
        }
        foreach (ScenarioSceneSaveDataV1 value in data.Scenes)
        {
            RequireItem(value, "Scene");
            SceneState state = value.State switch
            {
                "Preparing" or "Ready" => SceneState.Binding,
                "AwaitingWriting" => SceneState.Processing,
                "Settled" => SceneState.Settled,
                "Cancelled" => throw new InvalidDataException("旧版 Cancelled Scene 没有无歧义迁移路径，请先使用旧版本删除该 Scene。"),
                _ when Enum.TryParse(value.State, false, out SceneState parsed) && Enum.IsDefined(parsed) => parsed,
                _ => throw new InvalidDataException($"Scene.State“{value.State}”无效。")
            };
            if (value.Bindings is null)
                throw new InvalidDataException("Scene.Bindings 不能为 null。");
            SceneSlotBinding[] bindings = value.Bindings.Select(MapBinding).ToArray();
            SceneSlotSpecification[] slots = (value.Slots ?? []).Select(MapSlot).ToArray();
            var scene = new Scene(value.Id, Parse(value.DefinitionId, "Scene.DefinitionId", text => new SceneDefinitionType(text)), RequireText(value.ModuleId, "Scene.ModuleId"), RequireText(value.ModuleVersion, "Scene.ModuleVersion"), value.BasedOnScenarioStateId, RequireText(value.Name, "Scene.Name"), RequireValue(value.Description, "Scene.Description"), (SceneSettlementOptions)value.SettlementOptions, state, slots, bindings, value.DefinitionFrozen);
            Add(snapshot.Scenes, scene.Id, scene, "Scene");
        }
        _ = RuntimeScenario.Create(snapshot);
        return snapshot;
    }

    private static T Parse<T>(string? value, string path, Func<string, T> factory)
    {
        string text = RequireText(value, path);
        try
        {
            return factory(text);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{path}“{text}”无效。", exception);
        }
    }

    private static void Add<T>(IDictionary<Guid, T> values, Guid id, T value, string entity)
    {
        if (!values.TryAdd(id, value))
            throw new InvalidDataException($"{entity} Id {id} 重复。");
    }

    private static SceneSlotBinding MapBinding(ScenarioSceneBindingSaveDataV1 binding)
    {
        if (binding is null)
            throw new InvalidDataException("Scene.Binding 存档项不能为 null。");
        return new SceneSlotBinding(RequireText(binding.SlotId, "Scene.Binding.SlotId"), binding.ElementIds ?? throw new InvalidDataException("Scene.Binding.ElementIds 不能为 null。"));
    }

    private static SceneSlotSpecification MapSlot(ScenarioSceneSlotSaveDataV1 slot)
    {
        if (slot is null)
            throw new InvalidDataException("Scene.Slot 存档项不能为 null。");
        return new SceneSlotSpecification(RequireText(slot.Id, "Scene.Slot.Id"), RequireText(slot.Name, "Scene.Slot.Name"), RequireValue(slot.Description, "Scene.Slot.Description"), slot.Minimum, slot.Maximum, slot.ElementTypes ?? throw new InvalidDataException("Scene.Slot.ElementTypes 不能为 null。"), slot.RequiredAspectGroups ?? throw new InvalidDataException("Scene.Slot.RequiredAspectGroups 不能为 null。"));
    }

    private static void RequireItem(object? value, string entity)
    {
        if (value is null)
            throw new InvalidDataException($"{entity} 存档项不能为 null。");
    }

    private static double RequireFinite(double value, string path) => double.IsFinite(value) ? value : throw new InvalidDataException($"{path} 必须是有限 double。");
    private static string RequireText(string? value, string path) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"{path} 不能为空。");
    private static string RequireValue(string? value, string path) => value ?? throw new InvalidDataException($"{path} 不能为 null。");
}
