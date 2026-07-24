namespace Tavi.Domain.World;

/// <summary>表示 Aspect 和 Relation 成立的独立断言域；其具体语义由对应 Module 决定。</summary>
public sealed record Scope
{
    /// <summary>使用确定标识、断言域内容和必填 Owner Element 创建 Scope。</summary>
    /// <param name="id">Scope 标识。</param>
    /// <param name="name">供作者和 Module 识别的名称。</param>
    /// <param name="description">供诊断、作者或语言模型使用的说明。</param>
    /// <param name="quantity">由对应 Module 解释的有限强度。</param>
    /// <param name="type">由 Kernel 或 Module 定义的开放类型。</param>
    /// <param name="ownerElementId">持有该断言域的 Element 标识。</param>
    public Scope(Guid id, string name, string description, double quantity, ScopeType type, Guid ownerElementId)
    {
        Id = id;
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
        OwnerElementId = ownerElementId;
    }

    /// <summary>获取 Scope 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Scope 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Scope 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由对应 Module 解释的强度；它不替代 Scope 内各断言自己的强度。</summary>
    public double Quantity { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public ScopeType Type { get; private set; }

    /// <summary>获取持有该断言域的 Element 标识。</summary>
    public Guid OwnerElementId { get; }

    /// <summary>更新 Scope 名称；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateName(string name) => Name = name;

    /// <summary>更新 Scope 说明；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateDescription(string description) => Description = description;

    /// <summary>更新 Scope 强度；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateQuantity(double quantity) => Quantity = quantity;

    /// <summary>更新 Scope 类型；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateType(ScopeType type) => Type = type;
}
