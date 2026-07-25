namespace Tavi.Infrastructure.Persistence;

/// <summary>定义当前 World JSON 存档格式 V3。</summary>
internal sealed class SaveDocumentV3
{
    internal const int CurrentVersion = 3;

    /// <summary>获取或设置固定格式版本 3。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>获取或设置存档写入时间。</summary>
    public DateTimeOffset SavedAtUtc { get; set; }

    /// <summary>获取或设置 World 数据。</summary>
    public WorldSaveDataV3 World { get; set; } = new();
}

/// <summary>表示 V3 存档中的完整 World 快照。</summary>
internal sealed class WorldSaveDataV3
{
    /// <summary>获取或设置 World 状态标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置全部 Element。</summary>
    public List<ElementSaveDataV2> Elements { get; set; } = [];
    /// <summary>获取或设置全部 Scope。</summary>
    public List<ScopeSaveDataV3> Scopes { get; set; } = [];
    /// <summary>获取或设置全部 Aspect。</summary>
    public List<AspectSaveDataV3> Aspects { get; set; } = [];
    /// <summary>获取或设置全部 Relation。</summary>
    public List<RelationSaveDataV3> Relations { get; set; } = [];
    /// <summary>获取或设置全部 LocalAspect。</summary>
    public List<LocalAspectSaveDataV3> LocalAspects { get; set; } = [];
    /// <summary>获取或设置全部 LocalRelation。</summary>
    public List<LocalRelationSaveDataV3> LocalRelations { get; set; } = [];
}

/// <summary>表示 V3 存档中的 Scope。</summary>
internal sealed class ScopeSaveDataV3
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

/// <summary>表示 V3 存档中的 Aspect。</summary>
internal sealed class AspectSaveDataV3
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

/// <summary>表示 V3 存档中的 Relation。</summary>
internal sealed class RelationSaveDataV3
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

/// <summary>表示 V3 存档中的 LocalAspect。</summary>
internal sealed class LocalAspectSaveDataV3
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置自由谓词名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置自由谓词说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置整数数量。</summary>
    public int Quantity { get; set; }
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid ElementId { get; set; }
    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V3 存档中的 LocalRelation。</summary>
internal sealed class LocalRelationSaveDataV3
{
    /// <summary>获取或设置标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置自由谓词名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置自由谓词说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置整数数量。</summary>
    public int Quantity { get; set; }
    /// <summary>获取或设置来源 Element 标识。</summary>
    public Guid SourceElementId { get; set; }
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid TargetElementId { get; set; }
    /// <summary>获取或设置唯一 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}
