namespace Tavi.Domain.World;

/// <summary>表示一个 Scope 中从 Source Element 指向 Target Element 的规则化有向二元断言。</summary>
public sealed record Relation
{
    /// <summary>使用确定标识、整数数量、规则化类型、两个端点和唯一 Scope 创建 Relation。</summary>
    /// <param name="id">Relation 标识。</param><param name="quantity">由对应 Module 解释的整数数量。</param><param name="type">规则化开放类型。</param><param name="sourceElementId">来源 Element 标识。</param><param name="targetElementId">目标 Element 标识。</param><param name="scopeId">唯一所属 Scope 标识。</param>
    public Relation(Guid id, int quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId)
    {
        Id = id;
        Quantity = quantity;
        Type = type;
        SourceElementId = sourceElementId;
        TargetElementId = targetElementId;
        ScopeId = scopeId;
    }

    /// <summary>获取 Relation 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取由 Module 解释的规则化开放类型。</summary>
    public RelationType Type { get; private set; }

    /// <summary>获取来源 Element 标识。</summary>
    public Guid SourceElementId { get; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid TargetElementId { get; }

    /// <summary>获取唯一所属 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void UpdateQuantity(int quantity) => Quantity = quantity;
    internal void UpdateType(RelationType type) => Type = type;
}
