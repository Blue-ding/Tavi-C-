namespace Tavi.Extensibility;

/// <summary>指定 SceneDefinition 支持的结算路径。</summary>
[Flags]
public enum SceneSettlementCapabilities
{
    /// <summary>SceneDefinition 尚未声明任何可用结算路径。</summary>
    None = 0,

    /// <summary>Scene 可以由所属 Module 的规则结算器产生确定的 Scenario 变化。</summary>
    Rules = 1,

    /// <summary>Scene 可以进入允许玩家改变目标的 Writing 演绎路径。</summary>
    Writing = 2
}

/// <summary>描述 Scene 槽位接受 Element 时使用的声明式条件。</summary>
public sealed record SceneSlotRequirement
{
    /// <summary>获取允许填入的 Element 类型；空集合表示不附加类型限制。</summary>
    public IReadOnlySet<SemanticKey> ElementTypes { get; init; } = new HashSet<SemanticKey>();

    /// <summary>获取 Element 必须具有的 Aspect Group；空集合表示不附加 Group 条件。</summary>
    public IReadOnlySet<SemanticKey> RequiredAspectGroups { get; init; } = new HashSet<SemanticKey>();
}

/// <summary>描述 SceneDefinition 中一个可以绑定 Scenario Element 的稳定槽位。</summary>
public sealed record SceneSlotDefinition
{
    /// <summary>获取槽位在当前 SceneDefinition 中的稳定本地标识。</summary>
    public required string Id { get; init; }

    /// <summary>获取面向玩家或作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向玩家、作者或语言模型的说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取允许绑定的最少 Element 数。</summary>
    public required int Minimum { get; init; }

    /// <summary>获取允许绑定的最多 Element 数；不设上限时为 null。</summary>
    public int? Maximum { get; init; }

    /// <summary>获取该槽位的结构化 Element 条件。</summary>
    public SceneSlotRequirement Requirement { get; init; } = new();
}

/// <summary>描述 Module 可以提供并由 Evolution 实例化为 Scene 的功能定义。</summary>
public sealed record SceneDefinition
{
    /// <summary>获取 SceneDefinition 稳定键。</summary>
    public required SemanticKey Id { get; init; }

    /// <summary>获取拥有该定义的 Module。</summary>
    public required ModuleId Module { get; init; }

    /// <summary>获取解释该定义所需的 Module 版本。</summary>
    public required ModuleVersion ModuleVersion { get; init; }

    /// <summary>获取面向玩家或作者的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向玩家、作者或语言模型的说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取 Scene 支持的结算路径。</summary>
    public required SceneSettlementCapabilities SettlementCapabilities { get; init; }

    /// <summary>获取按稳定顺序排列的槽位定义。</summary>
    public IReadOnlyList<SceneSlotDefinition> Slots { get; init; } = [];

    /// <summary>获取产生该动态定义时所依据的 Scenario StateId；静态定义为 null。</summary>
    public Guid? SourceScenarioStateId { get; init; }
}
