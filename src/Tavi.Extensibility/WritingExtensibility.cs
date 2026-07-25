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

/// <summary>表示未来 Writing 演绎完成后交给 Module 的独立结果。</summary>
public sealed record WrittenSceneOutcomeContext
{
    /// <summary>获取与真实 Scenario 隔离的 Scene 局部视图。</summary>
    public required SceneContextView SceneContext { get; init; }

    /// <summary>获取玩家最终接受的文字结果。</summary>
    public required string Text { get; init; }
}
