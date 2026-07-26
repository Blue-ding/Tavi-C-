using Tavi.Extensibility;

namespace Tavi.Application.Extensions.Performance;

/// <summary>由 Module Plugin 实现，负责 Scene 展开、Beat 定义、Beat 解决与最终 Scene 结算提案。</summary>
public interface IPerformanceExtension
{
    ValueTask<PerformanceExpansionProposal> ExpandAsync(PerformanceExpansionContext context, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(BeatDefinitionContext context, CancellationToken cancellationToken);
    ValueTask<BeatResolutionProposal> ResolveBeatAsync(BeatResolutionContext context, CancellationToken cancellationToken);
    ValueTask<SceneSettlementProposal> CompleteAsync(PerformanceCompletionContext context, CancellationToken cancellationToken);
}

public sealed record PerformanceExpansionContext
{
    public required SceneContextView Scene { get; init; }
    public required long RandomSeed { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

public sealed record BeatDefinitionContext
{
    public required IPerformanceView Performance { get; init; }
    public required long RandomSeed { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

public sealed record BeatResolutionContext
{
    public required IPerformanceView Performance { get; init; }
    public required PerformanceBeatView Beat { get; init; }
    public string Interaction { get; init; } = string.Empty;
    public required long RandomSeed { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

public sealed record PerformanceCompletionContext
{
    public required SceneContextView Scene { get; init; }
    public required IPerformanceView Performance { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}
