namespace Tavi.Domain.World;

/// <summary>表示一个 Scope 中针对单个 Element 的一元断言。</summary>
public sealed record Aspect
{
    /// <summary>使用确定标识、断言内容、目标 Element 和唯一 Scope 创建 Aspect。</summary>
    /// <param name="id">Aspect 断言实例标识。</param>
    /// <param name="name">供作者和 Module 识别的名称。</param>
    /// <param name="description">供诊断、作者或语言模型使用的说明。</param>
    /// <param name="quantity">由对应 Module 解释的有限强度。</param>
    /// <param name="type">由 Kernel 或 Module 定义的开放类型。</param>
    /// <param name="elementId">该一元断言指向的 Element 标识。</param>
    /// <param name="scopeId">该断言唯一所属的 Scope 标识；跨 Scope 语义通过复制断言表达。</param>
    public Aspect(Guid id, string name, string description, double quantity, AspectType type, Guid elementId, Guid scopeId)
    {
        Id = id;
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
        ElementId = elementId;
        ScopeId = scopeId;
    }

    /// <summary>获取 Aspect 断言实例标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Aspect 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Aspect 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由对应 Module 解释的强度；World Kernel 只保证该值有限。</summary>
    public double Quantity { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public AspectType Type { get; private set; }

    /// <summary>获取该一元断言指向的 Element 标识。</summary>
    public Guid ElementId { get; }

    /// <summary>获取该断言唯一所属的 Scope 标识。</summary>
    public Guid ScopeId { get; }

    /// <summary>更新 Aspect 名称；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateName(string name) => Name = name;

    /// <summary>更新 Aspect 说明；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateDescription(string description) => Description = description;

    /// <summary>更新 Aspect 强度；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateQuantity(double quantity) => Quantity = quantity;

    /// <summary>更新 Aspect 类型；调用方必须位于 World 事务边界内。</summary>
    internal void UpdateType(AspectType type) => Type = type;
}
