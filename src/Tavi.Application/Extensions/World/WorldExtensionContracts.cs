using Tavi.Extensibility;

namespace Tavi.Application.Extensions.World;

/// <summary>由 Module Plugin 实现，为 World 提供组合式创作操作。</summary>
public interface IWorldAuthoringExtension
{
    /// <summary>依据当前只读 World 返回可用操作。</summary>
    ValueTask<IReadOnlyList<WorldAuthoringAction>> GetActionsAsync(IWorldView world, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
    /// <summary>执行一个操作并返回不直接修改 World 的原子提案。</summary>
    ValueTask<WorldAuthoringProposal> ProposeAsync(WorldAuthoringRequest request, CancellationToken cancellationToken);
}
