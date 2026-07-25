namespace Tavi.Domain.World;

/// <summary>表示一个 Scope 中针对单个 Element 的规则化一元断言。</summary>
public sealed record Aspect
{
    /// <summary>使用确定标识、整数数量、规则化类型、目标 Element 和唯一 Scope 创建 Aspect。</summary>
    /// <param name="id">Aspect 标识。</param><param name="quantity">由对应 Module 解释的整数数量。</param><param name="type">规则化开放类型。</param><param name="elementId">目标 Element 标识。</param><param name="scopeId">唯一所属 Scope 标识。</param>
    public Aspect(Guid id, int quantity, AspectType type, Guid elementId, Guid scopeId)
    {
        Id = id;
        Quantity = quantity;
        Type = type;
        ElementId = elementId;
        ScopeId = scopeId;
    }

    /// <summary>获取 Aspect 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取由 Module 解释的规则化开放类型。</summary>
    public AspectType Type { get; private set; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid ElementId { get; }

    /// <summary>获取唯一所属 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void UpdateQuantity(int quantity) => Quantity = quantity;
    internal void UpdateType(AspectType type) => Type = type;
}
