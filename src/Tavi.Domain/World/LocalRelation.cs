namespace Tavi.Domain.World;

/// <summary>表示仅存在于 World、由玩家和 Guidance 解释且不承担 Evolution 语义的有向二元叙事事实。</summary>
public sealed record LocalRelation
{
    /// <summary>使用确定标识、自由谓词文本、整数数量、两个端点和唯一 Scope 创建 LocalRelation。</summary>
    /// <param name="id">LocalRelation 标识。</param><param name="name">自由谓词名称。</param><param name="description">自由谓词说明。</param><param name="quantity">由玩家与 Guidance 解释的整数数量。</param><param name="sourceElementId">来源 Element 标识。</param><param name="targetElementId">目标 Element 标识。</param><param name="scopeId">唯一所属 Scope 标识。</param>
    public LocalRelation(Guid id, string name, string description, int quantity, Guid sourceElementId, Guid targetElementId, Guid scopeId)
    {
        Id = id;
        Name = name;
        Description = description;
        Quantity = quantity;
        SourceElementId = sourceElementId;
        TargetElementId = targetElementId;
        ScopeId = scopeId;
    }

    /// <summary>获取 LocalRelation 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取自由谓词名称。</summary>
    public string Name { get; private set; }

    /// <summary>获取自由谓词说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由玩家与 Guidance 解释的整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取来源 Element 标识。</summary>
    public Guid SourceElementId { get; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid TargetElementId { get; }

    /// <summary>获取唯一所属 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void UpdateName(string name) => Name = name;
    internal void UpdateDescription(string description) => Description = description;
    internal void UpdateQuantity(int quantity) => Quantity = quantity;
}
