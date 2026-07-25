namespace Tavi.Infrastructure.Persistence;

/// <summary>定义独立 Scenario JSON 存档格式 V1。</summary>
internal sealed class ScenarioSaveDocumentV1
{
    internal const int CurrentVersion = 1;

    /// <summary>获取或设置固定格式版本 1。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>获取或设置存档写入时间。</summary>
    public DateTimeOffset SavedAtUtc { get; set; }

    /// <summary>获取或设置完整 Scenario 数据。</summary>
    public ScenarioSaveDataV1 Scenario { get; set; } = new();
}

/// <summary>表示 V1 存档中的完整 Scenario 快照。</summary>
internal sealed class ScenarioSaveDataV1
{
    /// <summary>获取或设置 Scenario StateId。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置来源 World StateId。</summary>
    public Guid SourceWorldStateId { get; set; }

    /// <summary>获取或设置所需 Module。</summary>
    public List<ScenarioModuleSaveDataV1> Modules { get; set; } = [];

    /// <summary>获取或设置全部 Element。</summary>
    public List<ScenarioElementSaveDataV1> Elements { get; set; } = [];

    /// <summary>获取或设置全部 Scope。</summary>
    public List<ScenarioScopeSaveDataV1> Scopes { get; set; } = [];

    /// <summary>获取或设置全部 Aspect。</summary>
    public List<ScenarioAspectSaveDataV1> Aspects { get; set; } = [];

    /// <summary>获取或设置全部 Relation。</summary>
    public List<ScenarioRelationSaveDataV1> Relations { get; set; } = [];

    /// <summary>获取或设置全部 Scene。</summary>
    public List<ScenarioSceneSaveDataV1> Scenes { get; set; } = [];
}

/// <summary>表示 V1 Scenario 存档中的 Module 引用。</summary>
internal sealed class ScenarioModuleSaveDataV1
{
    /// <summary>获取或设置 Module 标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>获取或设置 Module 版本。</summary>
    public string Version { get; set; } = string.Empty;
}

/// <summary>表示 V1 Scenario 存档中的 Element。</summary>
internal sealed class ScenarioElementSaveDataV1
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>获取或设置开放类型。</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>表示 V1 Scenario 存档中的 Scope。</summary>
internal sealed class ScenarioScopeSaveDataV1
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }

    /// <summary>获取或设置开放类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置 Owner Element 标识。</summary>
    public Guid OwnerElementId { get; set; }
}

/// <summary>表示 V1 Scenario 存档中的 Aspect。</summary>
internal sealed class ScenarioAspectSaveDataV1
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }

    /// <summary>获取或设置开放类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid ElementId { get; set; }

    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V1 Scenario 存档中的 Relation。</summary>
internal sealed class ScenarioRelationSaveDataV1
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }

    /// <summary>获取或设置开放类型。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>获取或设置来源 Element 标识。</summary>
    public Guid SourceElementId { get; set; }

    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid TargetElementId { get; set; }

    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V1 Scenario 存档中的 Scene。</summary>
internal sealed class ScenarioSceneSaveDataV1
{
    /// <summary>获取或设置 Scene 标识。</summary>
    public Guid Id { get; set; }

    /// <summary>获取或设置 SceneDefinition 键。</summary>
    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>获取或设置 Module 标识。</summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>获取或设置 Module 版本。</summary>
    public string ModuleVersion { get; set; } = string.Empty;

    /// <summary>获取或设置 SceneDefinition 所依据的 Scenario StateId。</summary>
    public Guid BasedOnScenarioStateId { get; set; }

    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>获取或设置结算能力标志。</summary>
    public int SettlementOptions { get; set; }

    /// <summary>获取或设置 SceneState 文本。</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>获取或设置全部槽位绑定。</summary>
    public List<ScenarioSceneBindingSaveDataV1> Bindings { get; set; } = [];
}

/// <summary>表示 V1 Scenario 存档中的 Scene 槽位绑定。</summary>
internal sealed class ScenarioSceneBindingSaveDataV1
{
    /// <summary>获取或设置槽位标识。</summary>
    public string SlotId { get; set; } = string.Empty;

    /// <summary>获取或设置按绑定顺序排列的 Element 标识。</summary>
    public List<Guid> ElementIds { get; set; } = [];
}
