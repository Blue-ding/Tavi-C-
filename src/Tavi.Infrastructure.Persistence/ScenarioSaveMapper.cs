using Tavi.Domain.Scenario;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Infrastructure.Persistence;

/// <summary>在 ScenarioSnapshot 与版本化 JSON DTO 之间执行显式双向映射。</summary>
internal static class ScenarioSaveMapper
{
    internal static ScenarioSaveDataV2 FromDomain(ScenarioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ScenarioSaveDataV2
        {
            Id = snapshot.Id,
            SourceWorldStateId = snapshot.SourceWorldStateId,
            Modules = snapshot.Modules.Select(MapModule).ToList(),
            Elements = snapshot.Elements.Values.Select(MapElement).ToList(),
            Scopes = snapshot.Scopes.Values.Select(value => new ScenarioScopeSaveDataV2 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToList(),
            Aspects = snapshot.Aspects.Values.Select(value => new ScenarioAspectSaveDataV2 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToList(),
            Relations = snapshot.Relations.Values.Select(value => new ScenarioRelationSaveDataV2 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToList(),
            Scenes = snapshot.Scenes.Values.Select(MapScene).ToList()
        };
    }

    internal static ScenarioSnapshot ToDomain(ScenarioSaveDataV2 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Modules is null || data.Elements is null || data.Scopes is null || data.Aspects is null || data.Relations is null || data.Scenes is null)
            throw new InvalidDataException("Scenario V2 存档集合不能为 null。");
        var snapshot = CreateBase(data.Id, data.SourceWorldStateId, data.Modules, data.Elements);
        foreach (ScenarioScopeSaveDataV2 value in data.Scopes)
            Add(snapshot.Scopes, Require(value, "Scope").Id, new Scope(value.Id, value.Quantity, Parse(value.Type, "Scope.Type", text => new ScopeType(text)), value.OwnerElementId), "Scope");
        foreach (ScenarioAspectSaveDataV2 value in data.Aspects)
            Add(snapshot.Aspects, Require(value, "Aspect").Id, new Aspect(value.Id, value.Quantity, Parse(value.Type, "Aspect.Type", text => new AspectType(text)), value.ElementId, value.ScopeId), "Aspect");
        foreach (ScenarioRelationSaveDataV2 value in data.Relations)
            Add(snapshot.Relations, Require(value, "Relation").Id, new Relation(value.Id, value.Quantity, Parse(value.Type, "Relation.Type", text => new RelationType(text)), value.SourceElementId, value.TargetElementId, value.ScopeId), "Relation");
        AddScenes(snapshot, data.Scenes);
        _ = RuntimeScenario.Create(snapshot);
        return snapshot;
    }

    internal static ScenarioSnapshot MigrateFromV1(ScenarioSaveDataV1 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Modules is null || data.Elements is null || data.Scopes is null || data.Aspects is null || data.Relations is null || data.Scenes is null)
            throw new InvalidDataException("Scenario V1 存档集合不能为 null。");
        var snapshot = CreateBase(data.Id, data.SourceWorldStateId, data.Modules, data.Elements);
        foreach (ScenarioScopeSaveDataV1 value in data.Scopes)
            Add(snapshot.Scopes, Require(value, "Scope").Id, new Scope(value.Id, RequireInteger(value.Quantity, "Scope.Quantity"), Parse(value.Type, "Scope.Type", text => new ScopeType(text)), value.OwnerElementId), "Scope");
        foreach (ScenarioAspectSaveDataV1 value in data.Aspects)
            Add(snapshot.Aspects, Require(value, "Aspect").Id, new Aspect(value.Id, RequireInteger(value.Quantity, "Aspect.Quantity"), Parse(value.Type, "Aspect.Type", text => new AspectType(text)), value.ElementId, value.ScopeId), "Aspect");
        foreach (ScenarioRelationSaveDataV1 value in data.Relations)
            Add(snapshot.Relations, Require(value, "Relation").Id, new Relation(value.Id, RequireInteger(value.Quantity, "Relation.Quantity"), Parse(value.Type, "Relation.Type", text => new RelationType(text)), value.SourceElementId, value.TargetElementId, value.ScopeId), "Relation");
        AddScenes(snapshot, data.Scenes);
        _ = RuntimeScenario.Create(snapshot);
        return snapshot;
    }

    private static ScenarioSnapshot CreateBase(Guid id, Guid sourceWorldStateId, IEnumerable<ScenarioModuleSaveDataV1> modules, IEnumerable<ScenarioElementSaveDataV1> elements)
    {
        var snapshot = new ScenarioSnapshot { Id = id, SourceWorldStateId = sourceWorldStateId };
        foreach (ScenarioModuleSaveDataV1 value in modules)
        {
            Require(value, "Module");
            snapshot.Modules.Add(new ScenarioModuleReference(RequireText(value.Id, "Module.Id"), RequireText(value.Version, "Module.Version"), value.Parameters ?? new Dictionary<string, string>()));
        }
        foreach (ScenarioElementSaveDataV1 value in elements)
            Add(snapshot.Elements, Require(value, "Element").Id, new Element(value.Id, RequireText(value.Name, "Element.Name"), RequireValue(value.Description, "Element.Description"), Parse(value.Type, "Element.Type", text => new ElementType(text))), "Element");
        return snapshot;
    }

    private static void AddScenes(ScenarioSnapshot snapshot, IEnumerable<ScenarioSceneSaveDataV1> scenes)
    {
        foreach (ScenarioSceneSaveDataV1 value in scenes)
        {
            Require(value, "Scene");
            SceneState state = value.State switch
            {
                "Preparing" or "Ready" => SceneState.Binding,
                "AwaitingWriting" => SceneState.Processing,
                "Settled" => SceneState.Settled,
                "Cancelled" => throw new InvalidDataException("旧版 Cancelled Scene 没有无歧义迁移路径，请先使用旧版本删除该 Scene。"),
                _ when Enum.TryParse(value.State, false, out SceneState parsed) && Enum.IsDefined(parsed) => parsed,
                _ => throw new InvalidDataException($"Scene.State“{value.State}”无效。")
            };
            SceneSlotBinding[] bindings = (value.Bindings ?? throw new InvalidDataException("Scene.Bindings 不能为 null。")).Select(MapBinding).ToArray();
            SceneSlotSpecification[] slots = (value.Slots ?? []).Select(MapSlot).ToArray();
            var scene = new Scene(value.Id, Parse(value.DefinitionId, "Scene.DefinitionId", text => new SceneDefinitionType(text)), RequireText(value.ModuleId, "Scene.ModuleId"), RequireText(value.ModuleVersion, "Scene.ModuleVersion"), value.BasedOnScenarioStateId, RequireText(value.Name, "Scene.Name"), RequireValue(value.Description, "Scene.Description"), (SceneSettlementOptions)value.SettlementOptions, state, slots, bindings, value.DefinitionFrozen);
            Add(snapshot.Scenes, scene.Id, scene, "Scene");
        }
    }

    private static ScenarioModuleSaveDataV1 MapModule(ScenarioModuleReference value) => new() { Id = value.Id, Version = value.Version, Parameters = new Dictionary<string, string>(value.Parameters, StringComparer.Ordinal) };
    private static ScenarioElementSaveDataV1 MapElement(Element value) => new() { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value };
    private static ScenarioSceneSaveDataV1 MapScene(Scene value) => new()
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
    };

    private static SceneSlotBinding MapBinding(ScenarioSceneBindingSaveDataV1 value)
    {
        Require(value, "Scene.Binding");
        return new SceneSlotBinding(RequireText(value.SlotId, "Scene.Binding.SlotId"), value.ElementIds ?? throw new InvalidDataException("Scene.Binding.ElementIds 不能为 null。"));
    }

    private static SceneSlotSpecification MapSlot(ScenarioSceneSlotSaveDataV1 value)
    {
        Require(value, "Scene.Slot");
        return new SceneSlotSpecification(RequireText(value.Id, "Scene.Slot.Id"), RequireText(value.Name, "Scene.Slot.Name"), RequireValue(value.Description, "Scene.Slot.Description"), value.Minimum, value.Maximum, value.ElementTypes ?? throw new InvalidDataException("Scene.Slot.ElementTypes 不能为 null。"), value.RequiredAspectGroups ?? throw new InvalidDataException("Scene.Slot.RequiredAspectGroups 不能为 null。"));
    }

    private static T Parse<T>(string? value, string path, Func<string, T> factory)
    {
        string text = RequireText(value, path);
        try { return factory(text); }
        catch (ArgumentException exception) { throw new InvalidDataException($"{path}“{text}”无效。", exception); }
    }

    private static T Require<T>(T? value, string entity) where T : class => value ?? throw new InvalidDataException($"{entity} 存档项不能为 null。");
    private static void Add<T>(IDictionary<Guid, T> values, Guid id, T value, string entity)
    {
        if (!values.TryAdd(id, value))
            throw new InvalidDataException($"{entity} Id {id} 重复。");
    }

    private static int RequireInteger(double value, string path)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException($"{path} 必须是 Int32 范围内的整数；旧存档值为 {value}。");
        return (int)value;
    }

    private static string RequireText(string? value, string path) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"{path} 不能为空。");
    private static string RequireValue(string? value, string path) => value ?? throw new InvalidDataException($"{path} 不能为 null。");
}
