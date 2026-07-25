namespace Tavi.Infrastructure.Persistence;

/// <summary>定义当前 Scenario JSON 存档格式 V2。</summary>
internal sealed class ScenarioSaveDocumentV2
{
    internal const int CurrentVersion = 2;

    /// <summary>获取或设置固定格式版本 2。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>获取或设置存档写入时间。</summary>
    public DateTimeOffset SavedAtUtc { get; set; }

    /// <summary>获取或设置完整 Scenario 数据。</summary>
    public ScenarioSaveDataV2 Scenario { get; set; } = new();
}

/// <summary>表示 V2 存档中的完整 Scenario 快照。</summary>
internal sealed class ScenarioSaveDataV2
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
    public List<ScenarioScopeSaveDataV2> Scopes { get; set; } = [];
    /// <summary>获取或设置全部 Aspect。</summary>
    public List<ScenarioAspectSaveDataV2> Aspects { get; set; } = [];
    /// <summary>获取或设置全部 Relation。</summary>
    public List<ScenarioRelationSaveDataV2> Relations { get; set; } = [];
    /// <summary>获取或设置全部 Scene。</summary>
    public List<ScenarioSceneSaveDataV1> Scenes { get; set; } = [];
}

/// <summary>表示 V2 Scenario 存档中的 Scope。</summary>
internal sealed class ScenarioScopeSaveDataV2
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置整数数量。</summary>
    public int Quantity { get; set; }
    /// <summary>获取或设置规则化类型。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置 Owner Element 标识。</summary>
    public Guid OwnerElementId { get; set; }
}

/// <summary>表示 V2 Scenario 存档中的 Aspect。</summary>
internal sealed class ScenarioAspectSaveDataV2
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置整数数量。</summary>
    public int Quantity { get; set; }
    /// <summary>获取或设置规则化类型。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid ElementId { get; set; }
    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V2 Scenario 存档中的 Relation。</summary>
internal sealed class ScenarioRelationSaveDataV2
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置整数数量。</summary>
    public int Quantity { get; set; }
    /// <summary>获取或设置规则化类型。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置来源 Element 标识。</summary>
    public Guid SourceElementId { get; set; }
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid TargetElementId { get; set; }
    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}
