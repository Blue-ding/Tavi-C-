namespace Tavi.Extensibility;

/// <summary>指定 Module 参数接受的稳定标量类型；参数不得改变持久化数据结构或既有数据解释。</summary>
public enum ModuleParameterType
{
    /// <summary>布尔值，规范文本为 true 或 false。</summary>
    Boolean,
    /// <summary>使用不变区域格式表示的整数。</summary>
    Integer,
    /// <summary>使用不变区域格式表示的有限浮点数。</summary>
    Number,
    /// <summary>不包含结构语义的普通文本。</summary>
    String
}

/// <summary>声明一个只影响 Module 运行行为且会随 ScenarioSession 冻结的配置参数。</summary>
public sealed record ModuleParameterDefinition
{
    /// <summary>获取 Module 内稳定的小写参数键。</summary>
    public required string Key { get; init; }
    /// <summary>获取面向玩家的名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取参数说明。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取参数标量类型。</summary>
    public required ModuleParameterType Type { get; init; }
    /// <summary>获取使用该类型规范格式表示的默认值。</summary>
    public required string DefaultValue { get; init; }
    /// <summary>获取可选的规范值白名单；空集合表示不限制。</summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
    /// <summary>获取数值最小值；非数值参数必须为 null。</summary>
    public double? Minimum { get; init; }
    /// <summary>获取数值最大值；非数值参数必须为 null。</summary>
    public double? Maximum { get; init; }

    /// <summary>获取字符串最小长度；非字符串参数必须为 null。</summary>
    public int? MinimumLength { get; init; }

    /// <summary>获取字符串最大长度；非字符串参数必须为 null。</summary>
    public int? MaximumLength { get; init; }

    /// <summary>获取该 Setting 的应用策略。</summary>
    public ModuleSettingApplyMode ApplyMode { get; init; } = ModuleSettingApplyMode.ProcessRestart;
}

/// <summary>声明一个 Module 对另一个 Module 的兼容版本依赖。</summary>
public sealed record ModuleDependency
{
    /// <summary>获取被依赖的 Module 标识。</summary>
    public required ModuleId Id { get; init; }

    /// <summary>获取允许的最低兼容版本。</summary>
    public required ModuleVersion MinimumVersion { get; init; }
}

/// <summary>描述一个声明式 Module 及其可选代码入口。</summary>
public sealed record ModuleManifest
{
    /// <summary>获取 Module 稳定标识；该标识同时拥有同名语义键命名空间。</summary>
    public required ModuleId Id { get; init; }

    /// <summary>获取 Module 持久化兼容版本。</summary>
    public required ModuleVersion Version { get; init; }

    /// <summary>获取声明文件 Schema 版本。</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>获取供作者和诊断界面使用的 Module 名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取供作者和诊断界面使用的说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取按声明顺序排列的 Module 依赖。</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];

    /// <summary>获取会在 ScenarioSession 创建时冻结的行为参数定义。</summary>
    public IReadOnlyList<ModuleParameterDefinition> Parameters { get; init; } = [];

}

/// <summary>聚合一个 Module 的 Manifest、语义定义和静态 Scene 定义。</summary>
public sealed record ModulePackageDefinition
{
    /// <summary>获取 Module Manifest。</summary>
    public required ModuleManifest Manifest { get; init; }

    /// <summary>获取声明式语义定义。</summary>
    public required SemanticModuleDefinition Semantics { get; init; }

    /// <summary>获取 Module 静态声明的 SceneDefinition。</summary>
    public IReadOnlyList<SceneDefinition> Scenes { get; init; } = [];

    /// <summary>获取 Module 的声明式 Setting Schema；未声明时为 null。</summary>
    public ModuleSettingsSchema? SettingsSchema { get; init; }
}
