namespace Tavi.Extensibility;

/// <summary>提供动态 SceneDefinition 时使用的不可变评估上下文。</summary>
public sealed record SceneDefinitionContext
{
    /// <summary>获取与真实 Scenario 隔离的只读视图。</summary>
    public required IScenarioView Scenario { get; init; }

    /// <summary>获取由显式调用方提供的可复现随机种子。</summary>
    public required long RandomSeed { get; init; }

    /// <summary>获取当前 Provider 所属 Module 在 ScenarioSession 创建时冻结的规范参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>提供规则结算 Scene 时使用的不可变上下文。</summary>
public sealed record SceneSettlementContext
{
    /// <summary>获取与真实 Scenario 隔离且只包含冻结 Scene 内部实体的局部视图。</summary>
    public required SceneContextView Context { get; init; }

    /// <summary>获取对应 SceneDefinition。</summary>
    public required SceneDefinition Definition { get; init; }

    /// <summary>获取由显式调用方提供的可复现随机种子。</summary>
    public required long RandomSeed { get; init; }

    /// <summary>获取规则结算器所属 Module 在 ScenarioSession 创建时冻结的规范参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>由 Plugin 实现，用于依据当前 Scenario 提供动态 SceneDefinition。</summary>
public interface ISceneDefinitionProvider
{
    /// <summary>获取实现所属的 Module。</summary>
    ModuleId Module { get; }

    /// <summary>评估当前 Scenario 并返回动态 SceneDefinition；实现不得保留上下文中的对象引用。</summary>
    ValueTask<IReadOnlyList<SceneDefinition>> EvaluateAsync(SceneDefinitionContext context, CancellationToken cancellationToken);
}

/// <summary>由 Plugin 实现，用于对所属 Module 的 Scene 执行确定性规则结算。</summary>
public interface ISceneRuleSettler
{
    /// <summary>获取实现所属的 Module。</summary>
    ModuleId Module { get; }

    /// <summary>获取该实现可以结算的 SceneDefinition。</summary>
    IReadOnlySet<SemanticKey> Definitions { get; }

    /// <summary>根据独立上下文产生 Scenario 修改提案；实现不得直接修改 Scenario。</summary>
    ValueTask<SceneSettlementProposal> SettleAsync(SceneSettlementContext context, CancellationToken cancellationToken);
}

/// <summary>定义 Plugin 向宿主注册可选能力时可使用的受限入口。</summary>
public interface IPluginRegistrar
{
    /// <summary>注册动态 SceneDefinition Provider。</summary>
    void AddSceneDefinitionProvider(ISceneDefinitionProvider provider);

    /// <summary>注册 Scene 规则结算器。</summary>
    void AddSceneRuleSettler(ISceneRuleSettler settler);

    /// <summary>注册 Writing 上下文贡献者；当前宿主可以选择不调度该能力。</summary>
    void AddWritingContextContributor(IWritingContextContributor contributor);

    /// <summary>注册 Writing 玩家互动策略；当前宿主可以选择不调度该能力。</summary>
    void AddWritingInteractionPolicy(IWritingInteractionPolicy policy);

    /// <summary>注册 Written Scene 结果贡献者；当前宿主可以选择不调度该能力。</summary>
    void AddWrittenSceneOutcomeContributor(IWrittenSceneOutcomeContributor contributor);
}

/// <summary>定义一个可选的代码 Plugin 入口；纯声明式 Module 不需要实现该接口。</summary>
public interface ITaviPlugin
{
    /// <summary>获取 Plugin 所属的主 Module。</summary>
    ModuleId Module { get; }

    /// <summary>获取 Plugin 与声明式 Module 一致的兼容版本。</summary>
    ModuleVersion Version { get; }

    /// <summary>向受限注册器注册细粒度能力；实现不得保存注册器引用。</summary>
    void Register(IPluginRegistrar registrar);
}
