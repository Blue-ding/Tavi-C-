namespace Tavi.Extensibility;

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

    /// <summary>获取可选 Plugin 程序集入口类型；纯声明式 Module 返回 null。</summary>
    public string? Entrypoint { get; init; }
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

    /// <summary>获取声明文件所在的规范化目录；内存定义没有目录时为 null。</summary>
    public string? SourceDirectory { get; init; }
}
