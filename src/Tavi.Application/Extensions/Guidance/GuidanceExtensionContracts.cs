using Tavi.Extensibility;

namespace Tavi.Application.Extensions.Guidance;

/// <summary>表示 Module 向 Guidance 提供的提示词片段。</summary>
public sealed record GuidanceInstructionContribution
{
    /// <summary>获取稳定贡献键。</summary>
    public required SemanticKey Id { get; init; }
    /// <summary>获取只描述 Module 语义与工具使用方式的指令文本。</summary>
    public required string Content { get; init; }
}

/// <summary>由 Module Plugin 实现，为 Guidance 提供提示词贡献；组合式工具复用 World Authoring 操作。</summary>
public interface IGuidanceExtension
{
    /// <summary>依据当前 World 返回相关提示词贡献。</summary>
    ValueTask<IReadOnlyList<GuidanceInstructionContribution>> GetInstructionsAsync(IWorldView world, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}
