namespace Tavi.Extensibility;

/// <summary>表示 Module 希望加入未来 Writing 上下文的一项结构化贡献。</summary>
public sealed record WritingContextContribution
{
    /// <summary>获取产生贡献的 Module。</summary>
    public required ModuleId Module { get; init; }

    /// <summary>获取贡献类型。</summary>
    public required SemanticKey Type { get; init; }

    /// <summary>获取供未来 Writing 宿主传递的结构化文本；程序不得解析自然语言来执行 Scenario 规则。</summary>
    public required string Content { get; init; }
}

/// <summary>表示未来 Writing 协调器向 Module 请求上下文时提供的数据。</summary>
public sealed record WritingContextRequest
{
    /// <summary>获取与真实 Scenario 隔离的 Scene 局部视图。</summary>
    public required SceneContextView Context { get; init; }
}

/// <summary>由 Plugin 实现，用于向未来 Writing 流程提供 Module 专属上下文。</summary>
public interface IWritingContextContributor
{
    /// <summary>获取实现所属的 Module。</summary>
    ModuleId Module { get; }

    /// <summary>产生结构化 Writing 上下文贡献；实现不得调用或持有 WritingSession。</summary>
    ValueTask<IReadOnlyList<WritingContextContribution>> ContributeAsync(WritingContextRequest request, CancellationToken cancellationToken);
}

/// <summary>表示 Module 为未来 Writing 玩家交互提供的一个选项。</summary>
public sealed record WritingInteractionOption
{
    /// <summary>获取选项稳定键。</summary>
    public required SemanticKey Id { get; init; }

    /// <summary>获取面向玩家的名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取面向玩家或语言模型的说明。</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>表示未来 Writing 协调器向 Module 请求玩家互动规则时提供的数据。</summary>
public sealed record WritingInteractionContext
{
    /// <summary>获取与真实 Scenario 隔离的 Scene 局部视图。</summary>
    public required SceneContextView SceneContext { get; init; }

    /// <summary>获取截至当前的玩家互动文本；Module 只能将其用于生成建议，不能据此直接修改 Scenario。</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];
}

/// <summary>由 Plugin 实现，用于影响未来 Writing 中可呈现的玩家互动方式。</summary>
public interface IWritingInteractionPolicy
{
    /// <summary>获取实现所属的 Module。</summary>
    ModuleId Module { get; }

    /// <summary>评估当前 Scene 并返回玩家互动选项。</summary>
    ValueTask<IReadOnlyList<WritingInteractionOption>> EvaluateAsync(WritingInteractionContext context, CancellationToken cancellationToken);
}

/// <summary>表示未来 Writing 演绎完成后交给 Module 的独立结果。</summary>
public sealed record WrittenSceneOutcomeContext
{
    /// <summary>获取与真实 Scenario 隔离的 Scene 局部视图。</summary>
    public required SceneContextView SceneContext { get; init; }

    /// <summary>获取玩家最终接受的文字结果。</summary>
    public required string Text { get; init; }
}

/// <summary>由 Plugin 实现，用于对未来 Writing 结果提出结构化 Scenario 修改建议。</summary>
public interface IWrittenSceneOutcomeContributor
{
    /// <summary>获取实现所属的 Module。</summary>
    ModuleId Module { get; }

    /// <summary>根据玩家接受的结果提出结构化修改；该能力与规则结算器相互独立。</summary>
    ValueTask<SceneSettlementProposal> ContributeAsync(WrittenSceneOutcomeContext context, CancellationToken cancellationToken);
}
