using Tavi.Extensibility;

namespace Tavi.Application.Extensions.Scenario;

/// <summary>提供动态 SceneDefinition 时使用的不可变评估上下文。</summary>
public sealed record SceneDefinitionContext
{
    /// <summary>获取与真实 Scenario 隔离的只读视图。</summary>
    public required IScenarioView Scenario { get; init; }
    /// <summary>获取由调用方提供的可复现随机种子。</summary>
    public required long RandomSeed { get; init; }
    /// <summary>获取能力所属 Module 的冻结参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>提供规则结算 Scene 时使用的不可变上下文。</summary>
public sealed record SceneSettlementContext
{
    /// <summary>获取只包含冻结 Scene 内部实体的局部视图。</summary>
    public required SceneContextView Context { get; init; }
    /// <summary>获取对应 SceneDefinition。</summary>
    public required SceneDefinition Definition { get; init; }
    /// <summary>获取由调用方提供的可复现随机种子。</summary>
    public required long RandomSeed { get; init; }
    /// <summary>获取能力所属 Module 的冻结参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>由 Module Plugin 实现，用于依据当前 Scenario 提供动态 SceneDefinition。</summary>
public interface IScenarioDefinitionExtension
{
    /// <summary>评估当前 Scenario 并返回动态 SceneDefinition；实现不得保留上下文对象。</summary>
    ValueTask<IReadOnlyList<SceneDefinition>> EvaluateAsync(SceneDefinitionContext context, CancellationToken cancellationToken);
}

/// <summary>由 Module Plugin 实现，用于路由所属 Module 的确定性 Scene 规则结算。</summary>
public interface IScenarioSettlementExtension
{
    /// <summary>获取该入口可以结算的 SceneDefinition。</summary>
    IReadOnlySet<SemanticKey> Definitions { get; }
    /// <summary>根据独立局部上下文产生 Scenario 修改提案。</summary>
    ValueTask<SceneSettlementProposal> SettleAsync(SceneSettlementContext context, CancellationToken cancellationToken);
}
