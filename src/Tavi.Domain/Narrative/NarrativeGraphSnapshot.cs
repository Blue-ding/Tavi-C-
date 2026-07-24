namespace Tavi.Domain.Narrative;

/// <summary>表示某一 Narrative Tick 的独立图快照。它只保存有界工作集，不保存全部 World 历史或所有可能故事状态。</summary>
public sealed record NarrativeGraphSnapshot
{
    /// <summary>获取或设置图状态标识；每次未来的 Kernel 实际提交图变更后应生成新值。</summary>
    public Guid StateId { get; init; } = Guid.NewGuid();

    /// <summary>获取或设置构造该图时使用的 World 状态标识。</summary>
    public required Guid SourceWorldStateId { get; init; }

    /// <summary>获取或设置当前演化 Tick；Tick 仅用于局部演化，不代表需要保留历史快照。</summary>
    public long Tick { get; init; }

    /// <summary>获取或设置创建该状态的 Narrative Profile 标识。</summary>
    public required NarrativeTypeKey Profile { get; init; }

    /// <summary>获取或设置 Profile 的持久化兼容版本。</summary>
    public required int ProfileVersion { get; init; }

    /// <summary>获取或设置用于复现随机选择的种子。</summary>
    public required long RandomSeed { get; init; }

    /// <summary>获取或设置按标识索引的全部活跃节点。</summary>
    public IReadOnlyDictionary<Guid, NarrativeNode> Nodes { get; init; } = new Dictionary<Guid, NarrativeNode>();

    /// <summary>获取或设置按标识索引的全部活跃边。</summary>
    public IReadOnlyDictionary<Guid, NarrativeLink> Links { get; init; } = new Dictionary<Guid, NarrativeLink>();
}
