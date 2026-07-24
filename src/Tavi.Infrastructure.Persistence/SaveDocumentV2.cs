namespace Tavi.Infrastructure.Persistence;

/// <summary>定义当前 World JSON 存档格式；公开属性由 System.Text.Json 使用。</summary>
internal sealed class SaveDocumentV2
{
    internal const int CurrentVersion = 2;

    /// <summary>获取或设置固定格式版本 2。</summary>
    public int Version { get; set; } = CurrentVersion;
    /// <summary>获取或设置存档写入时间。</summary>
    public DateTimeOffset SavedAtUtc { get; set; }
    /// <summary>获取或设置 World 数据。</summary>
    public WorldSaveDataV2 World { get; set; } = new();
}

/// <summary>表示 V2 存档中的完整 World 快照。</summary>
internal sealed class WorldSaveDataV2
{
    /// <summary>获取或设置 World 状态标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置全部 Element。</summary>
    public List<ElementSaveDataV2> Elements { get; set; } = new();
    /// <summary>获取或设置全部 Aspect。</summary>
    public List<AspectSaveDataV2> Aspects { get; set; } = new();
    /// <summary>获取或设置全部 Relation。</summary>
    public List<RelationSaveDataV2> Relations { get; set; } = new();
    /// <summary>获取或设置全部 Scope。</summary>
    public List<ScopeSaveDataV2> Scopes { get; set; } = new();
}

/// <summary>表示 V2 存档中的 Element。</summary>
internal sealed class ElementSaveDataV2
{
    /// <summary>获取或设置实体标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置开放类型文本。</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>表示 V2 存档中唯一属于一个 Scope 的一元断言。</summary>
internal sealed class AspectSaveDataV2
{
    /// <summary>获取或设置实体标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }
    /// <summary>获取或设置开放类型文本。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid ElementId { get; set; }
    /// <summary>获取或设置唯一所属 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V2 存档中唯一属于一个 Scope 的有向二元断言。</summary>
internal sealed class RelationSaveDataV2
{
    /// <summary>获取或设置实体标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }
    /// <summary>获取或设置开放类型文本。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置来源 Element 标识。</summary>
    public Guid SourceElementId { get; set; }
    /// <summary>获取或设置目标 Element 标识。</summary>
    public Guid TargetElementId { get; set; }
    /// <summary>获取或设置唯一所属 Scope 标识。</summary>
    public Guid ScopeId { get; set; }
}

/// <summary>表示 V2 存档中由一个 Element 持有的断言域。</summary>
internal sealed class ScopeSaveDataV2
{
    /// <summary>获取或设置实体标识。</summary>
    public Guid Id { get; set; }
    /// <summary>获取或设置名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>获取或设置说明。</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>获取或设置有限强度。</summary>
    public double Quantity { get; set; }
    /// <summary>获取或设置开放类型文本。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>获取或设置 Owner Element 标识。</summary>
    public Guid OwnerElementId { get; set; }
}
