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

    /// <summary>获取 Element 名称。</summary>
    public string Name { get; private set; }

    /// <summary>获取 Element 说明。</summary>
    public string Description { get; private set; }

    /// <summary>获取规则化开放类型。</summary>
    public ElementType Type { get; private set; }

    internal void Update(string name, string description, ElementType type)
    {
        Name = name;
        Description = description;
        Type = type;
    }
}

/// <summary>表示 Scenario 中的规则化一元断言。</summary>
public sealed record Aspect
{
    /// <summary>使用确定标识、整数数量、规则化类型和结构引用创建 Aspect。</summary>
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

    /// <summary>获取整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取规则化开放类型。</summary>
    public AspectType Type { get; private set; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid ElementId { get; }

    /// <summary>获取唯一 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void Update(int quantity, AspectType type)
    {
        Quantity = quantity;
        Type = type;
    }
}

/// <summary>表示 Scenario 中的规则化有向二元断言。</summary>
public sealed record Relation
{
    /// <summary>使用确定标识、整数数量、规则化类型和结构引用创建 Relation。</summary>
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

    /// <summary>获取整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取规则化开放类型。</summary>
    public RelationType Type { get; private set; }

    /// <summary>获取来源 Element 标识。</summary>
    public Guid SourceElementId { get; }

    /// <summary>获取目标 Element 标识。</summary>
    public Guid TargetElementId { get; }

    /// <summary>获取唯一 Scope 标识。</summary>
    public Guid ScopeId { get; }

    internal void Update(int quantity, RelationType type)
    {
        Quantity = quantity;
        Type = type;
    }
}

/// <summary>表示 Scenario 中由一个 Element 持有的规则化断言域。</summary>
public sealed record Scope
{
    /// <summary>使用确定标识、整数数量、规则化类型和 Owner Element 创建 Scope。</summary>
    public Scope(Guid id, int quantity, ScopeType type, Guid ownerElementId)
    {
        Id = id;
        Quantity = quantity;
        Type = type;
        OwnerElementId = ownerElementId;
    }

    /// <summary>获取 Scope 标识。</summary>
    public Guid Id { get; }

    /// <summary>获取整数数量。</summary>
    public int Quantity { get; private set; }

    /// <summary>获取规则化开放类型。</summary>
    public ScopeType Type { get; private set; }

    /// <summary>获取 Owner Element 标识。</summary>
    public Guid OwnerElementId { get; }

    internal void Update(int quantity, ScopeType type)
    {
        Quantity = quantity;
        Type = type;
    }
}
