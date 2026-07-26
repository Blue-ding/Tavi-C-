namespace Tavi.Domain;

/// <summary>表示由一个 Element 持有并容纳规则化事实的结构化断言域。</summary>
public sealed record Scope
{
    /// <summary>使用确定标识、整数数量、规则化类型和必填 Owner Element 创建 Scope。</summary>
    /// <param name="id">Scope 标识。</param><param name="quantity">由对应 Module 解释的整数数量。</param><param name="type">规则化开放类型。</param><param name="ownerElementId">Owner Element 标识。</param>
    public Scope(Guid id, int quantity, ScopeType type, Guid ownerElementId)
    {
        Id = id;
        Quantity = quantity;
        Type = type;
        OwnerElementId = ownerElementId;
    }

    /// <summary>获取 Scope 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取由 Module 解释的规则化开放类型。</summary>
    public ScopeType Type { get; private set; }

    /// <summary>获取 Owner Element 标识。</summary>
    public Guid OwnerElementId { get; }

    internal void UpdateQuantity(int quantity) => Quantity = quantity;
    internal void UpdateType(ScopeType type) => Type = type;
}
