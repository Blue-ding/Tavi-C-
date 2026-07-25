using Tavi.Extensibility;

namespace Tavi.Application.Extensions.Writing;

/// <summary>由 Module Plugin 实现，用于向 Writing 流程提供 Module 专属上下文。</summary>
public interface IWritingContextExtension
{
    /// <summary>产生结构化 Writing 上下文贡献；实现不得调用或持有 WritingSession。</summary>
    ValueTask<IReadOnlyList<WritingContextContribution>> ContributeAsync(WritingContextRequest request, CancellationToken cancellationToken);
}

/// <summary>由 Module Plugin 实现，用于影响 Writing 中可呈现的玩家互动方式。</summary>
public interface IWritingInteractionExtension
{
    /// <summary>评估当前 Scene 并返回玩家互动选项。</summary>
    ValueTask<IReadOnlyList<WritingInteractionOption>> EvaluateAsync(WritingInteractionContext context, CancellationToken cancellationToken);
}

/// <summary>由 Module Plugin 实现，用于把玩家接受的 Written Scene 结果转换为局部结构化提案。</summary>
public interface IWrittenSceneOutcomeExtension
{
    /// <summary>根据独立结果产生局部结构化修改；实现不得直接修改 Scenario。</summary>
    ValueTask<SceneSettlementProposal> ContributeAsync(WrittenSceneOutcomeContext context, CancellationToken cancellationToken);
}
