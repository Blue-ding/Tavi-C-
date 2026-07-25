namespace Tavi.Extensibility;

/// <summary>指定声明式约束的核心解释方式。</summary>
public enum SemanticConstraintKind
{
    /// <summary>要求指定 Element 类型拥有一定数量的指定 Scope。</summary>
    OwnedScopeCardinality,

    /// <summary>要求指定 Element 在指定 Scope 中拥有一定数量的 Aspect Group 成员。</summary>
    AspectGroupCardinality
}

/// <summary>描述一个开放 Element 类型。</summary>
public sealed record ElementTypeDefinition
{
    /// <summary>获取由当前 Module 拥有的类型键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取拥有该类型键的 Module。</summary>
    public ModuleId Module => Key.Namespace;

    /// <summary>获取面向作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向作者、诊断或语言模型的说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取供其他声明引用的稳定分类标签。</summary>
    public IReadOnlySet<SemanticKey> Tags { get; init; } = new HashSet<SemanticKey>();
}

/// <summary>描述一个开放 Scope 类型。</summary>
public sealed record ScopeTypeDefinition
{
    /// <summary>获取由当前 Module 拥有的类型键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取拥有该类型键的 Module。</summary>
    public ModuleId Module => Key.Namespace;

    /// <summary>获取面向作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向作者、诊断或语言模型的说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取允许持有该 Scope 的 Element 类型；空集合表示不附加类型限制。</summary>
    public IReadOnlySet<SemanticKey> OwnerElementTypes { get; init; } = new HashSet<SemanticKey>();

    /// <summary>获取供其他声明引用的稳定分类标签。</summary>
    public IReadOnlySet<SemanticKey> Tags { get; init; } = new HashSet<SemanticKey>();
}

/// <summary>描述一组可以由当前或其他 Module 扩充的 Aspect 类型。</summary>
public sealed record AspectGroupDefinition
{
    /// <summary>获取 Aspect Group 稳定键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取面向作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取其他 Module 是否可以向该 Group 注册成员。</summary>
    public bool Extensible { get; init; }
}

/// <summary>描述一个开放 Aspect 类型。</summary>
public sealed record AspectTypeDefinition
{
    /// <summary>获取由当前 Module 拥有的类型键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取拥有该类型键的 Module。</summary>
    public ModuleId Module => Key.Namespace;

    /// <summary>获取面向作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向作者、诊断或语言模型的说明。</summary>
    public required string Description { get; init; }

    /// <summary>获取可选 Aspect Group；没有分组时为 null。</summary>
    public SemanticKey? Group { get; init; }

    /// <summary>获取允许作为 Aspect 目标的 Element 类型；空集合表示不附加类型限制。</summary>
    public IReadOnlySet<SemanticKey> SubjectElementTypes { get; init; } = new HashSet<SemanticKey>();

    /// <summary>获取 Quantity 允许的最小值；不限制时为 null。</summary>
    public double? MinimumQuantity { get; init; }

    /// <summary>获取 Quantity 允许的最大值；不限制时为 null。</summary>
    public double? MaximumQuantity { get; init; }

    /// <summary>获取供其他声明引用的稳定分类标签。</summary>
    public IReadOnlySet<SemanticKey> Tags { get; init; } = new HashSet<SemanticKey>();
}

/// <summary>描述一个开放 Relation 类型。</summary>
public sealed record RelationTypeDefinition
{
    /// <summary>获取由当前 Module 拥有的类型键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取拥有该类型键的 Module。</summary>
    public ModuleId Module => Key.Namespace;

    /// <summary>获取面向作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向作者、诊断或语言模型的说明。</summary>
    public required string Description { get; init; }

    /// <summary>获取允许作为来源的 Element 类型；空集合表示不附加类型限制。</summary>
    public IReadOnlySet<SemanticKey> SourceElementTypes { get; init; } = new HashSet<SemanticKey>();

    /// <summary>获取允许作为目标的 Element 类型；空集合表示不附加类型限制。</summary>
    public IReadOnlySet<SemanticKey> TargetElementTypes { get; init; } = new HashSet<SemanticKey>();

    /// <summary>获取 Quantity 允许的最小值；不限制时为 null。</summary>
    public double? MinimumQuantity { get; init; }

    /// <summary>获取 Quantity 允许的最大值；不限制时为 null。</summary>
    public double? MaximumQuantity { get; init; }

    /// <summary>获取供其他声明引用的稳定分类标签。</summary>
    public IReadOnlySet<SemanticKey> Tags { get; init; } = new HashSet<SemanticKey>();
}

/// <summary>描述一种由 ScenarioSession 在完整候选 Scenario 上统一执行的声明式语义约束。</summary>
public sealed record SemanticConstraintDefinition
{
    /// <summary>获取约束稳定键。</summary>
    public required SemanticKey Key { get; init; }

    /// <summary>获取约束解释方式。</summary>
    public required SemanticConstraintKind Kind { get; init; }

    /// <summary>获取约束适用的 Element 类型。</summary>
    public required SemanticKey SubjectElementType { get; init; }

    /// <summary>获取相关 Scope 类型。</summary>
    public required SemanticKey ScopeType { get; init; }

    /// <summary>获取 Aspect Group；仅 AspectGroupCardinality 约束需要该值。</summary>
    public SemanticKey? AspectGroup { get; init; }

    /// <summary>获取允许的最小数量。</summary>
    public required int Minimum { get; init; }

    /// <summary>获取允许的最大数量；不设置上限时为 null。</summary>
    public int? Maximum { get; init; }

    /// <summary>获取被统计的 Aspect 是否必须指向 Scope Owner。</summary>
    public bool AspectMustTargetScopeOwner { get; init; }

    /// <summary>获取违反约束时供人员理解的说明；程序不得解析该文本。</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>聚合一个 Module 的全部声明式 Scenario 语义。</summary>
public sealed record SemanticModuleDefinition
{
    /// <summary>获取 Element 类型定义。</summary>
    public IReadOnlyList<ElementTypeDefinition> ElementTypes { get; init; } = [];

    /// <summary>获取 Scope 类型定义。</summary>
    public IReadOnlyList<ScopeTypeDefinition> ScopeTypes { get; init; } = [];

    /// <summary>获取 Aspect Group 定义。</summary>
    public IReadOnlyList<AspectGroupDefinition> AspectGroups { get; init; } = [];

    /// <summary>获取 Aspect 类型定义。</summary>
    public IReadOnlyList<AspectTypeDefinition> AspectTypes { get; init; } = [];

    /// <summary>获取 Relation 类型定义。</summary>
    public IReadOnlyList<RelationTypeDefinition> RelationTypes { get; init; } = [];

    /// <summary>获取最终候选状态约束。</summary>
    public IReadOnlyList<SemanticConstraintDefinition> Constraints { get; init; } = [];
}
