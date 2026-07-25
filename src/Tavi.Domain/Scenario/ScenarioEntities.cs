namespace Tavi.Domain.Scenario;

/// <summary>表示 Scenario 中可以被断言和 Scene 引用的当前实体。</summary>
public sealed record Element
{
    /// <summary>使用确定标识和语义属性创建 Element。</summary>
    public Element(Guid id, string name, string description, ElementType type)
    {
        Id = id;
        Name = name;
        Description = description;
        Type = type;
    }

    /// <summary>获取 Element 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Element 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Element 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public ElementType Type { get; private set; }

    internal void Update(string name, string description, ElementType type)
    {
        Name = name;
        Description = description;
        Type = type;
    }
}

/// <summary>表示一个 Scope 中针对单个 Element 的一元断言。</summary>
public sealed record Aspect
{
    /// <summary>使用确定标识、语义属性、目标 Element 和唯一 Scope 创建 Aspect。</summary>
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

    /// <summary>获取 Aspect 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Aspect 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Aspect 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由对应 Module 解释的有限强度。</summary>
    public double Quantity { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public AspectType Type { get; private set; }

    /// <summary>获取该断言指向的 Element 标识。</summary>
    public Guid ElementId { get; }

    /// <summary>获取该断言唯一所属的 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void Update(string name, string description, double quantity, AspectType type)
    {
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
    }
}

/// <summary>表示一个 Scope 中从 Source Element 指向 Target Element 的有向二元断言。</summary>
public sealed record Relation
{
    /// <summary>使用确定标识、语义属性、两个端点和唯一 Scope 创建 Relation。</summary>
    public Relation(Guid id, string name, string description, double quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId)
    {
        Id = id;
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
        SourceElementId = sourceElementId;
        TargetElementId = targetElementId;
        ScopeId = scopeId;
    }

    /// <summary>获取 Relation 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取 Relation 名称；名称不承担唯一身份语义。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Relation 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取由对应 Module 解释的有限强度。</summary>
    public double Quantity { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public RelationType Type { get; private set; }

    /// <summary>获取来源 Element 标识。</summary>
    public Guid SourceElementId { get; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid TargetElementId { get; }

    /// <summary>获取该断言唯一所属的 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void Update(string name, string description, double quantity, RelationType type)
    {
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
    }
}

/// <summary>表示 Aspect 和 Relation 成立的独立断言域。</summary>
public sealed record Scope
{
    /// <summary>使用确定标识、语义属性和必填 Owner Element 创建 Scope。</summary>
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

    /// <summary>获取由对应 Module 解释的有限强度。</summary>
    public double Quantity { get; private set; }

    /// <summary>获取由 Module 解释的开放类型。</summary>
    public ScopeType Type { get; private set; }

    /// <summary>获取持有该断言域的 Element 标识。</summary>
    public Guid OwnerElementId { get; }

    internal void Update(string name, string description, double quantity, ScopeType type)
    {
        Name = name;
        Description = description;
        Quantity = quantity;
        Type = type;
    }
}
