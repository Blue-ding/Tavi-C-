namespace Tavi.Domain.Narrative;

/// <summary>指定 Beat 在当前 NarrativeState 中的生命周期；这些状态描述当前决策价值，而不是故事历史。</summary>
public enum NarrativeBeatState
{
    /// <summary>Beat 已存在，但当前不参与选择。</summary>
    Dormant,

    /// <summary>Beat 满足当前选择条件。</summary>
    Eligible,

    /// <summary>Beat 已被选入待生成的 SceneSeed。</summary>
    Selected,

    /// <summary>Beat 暂时被硬约束阻止。</summary>
    Blocked,

    /// <summary>Beat 已由 Scene 反馈结算，等待有限期冷却或裁剪。</summary>
    Resolved,

    /// <summary>Beat 已失去叙事价值，等待从工作集移除。</summary>
    Discarded
}

/// <summary>表示 NarrativeGraph 中具有稳定标识和模块特征的节点。</summary>
public abstract record NarrativeNode
{
    /// <summary>获取节点标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取按稳定特征键索引的扩展特征；核心算法不得通过解析特征名称以外的自然语言推断规则。</summary>
    public IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> Features { get; init; } = new Dictionary<NarrativeFeatureKey, NarrativeFeatureValue>();
}

/// <summary>表示一个当前仍有叙事价值的可演绎机会。Beat 被重化为节点，使多角色事件能够由一个中心节点及其邻域子图表达。</summary>
public sealed record NarrativeBeatNode : NarrativeNode
{
    /// <summary>获取供 LLM 与 Scene 使用的叙事意义；程序化演化策略不得解析该文本作出决定。</summary>
    public string Meaning { get; init; } = string.Empty;

    /// <summary>获取 Beat 当前生命周期状态。</summary>
    public NarrativeBeatState State { get; init; }

    /// <summary>获取跨故事类型通用的显著性，约定为零到一之间的有限值。</summary>
    public double Salience { get; init; }

    /// <summary>获取跨故事类型通用的张力，约定为零到一之间的有限值。</summary>
    public double Tension { get; init; }

    /// <summary>获取 Beat 延续既有推进方向的动量，约定为零到一之间的有限值。</summary>
    public double Momentum { get; init; }

    /// <summary>获取 Beat 相对近期内容的新颖度，约定为零到一之间的有限值。</summary>
    public double Novelty { get; init; }

    /// <summary>获取 Beat 已存在的 Tick 数。</summary>
    public long Age { get; init; }

    /// <summary>获取 Beat 尚需等待的 Tick 数。</summary>
    public long Cooldown { get; init; }

    /// <summary>获取创建 Beat 的 Tick。</summary>
    public long CreatedAtTick { get; init; }

    /// <summary>获取 Beat 最近一次被选中的 Tick；从未被选中时为空。</summary>
    public long? LastActivatedAtTick { get; init; }

    /// <summary>获取支持该 Beat 的 World Relation 标识；这些标识只提供来源追踪，不把 World 关系复制为 Narrative 状态。</summary>
    public IReadOnlySet<Guid> EvidenceRelationIds { get; init; } = new HashSet<Guid>();
}

/// <summary>表示 NarrativeGraph 对一个 World Anchor 的轻量引用。它不复制 Anchor 内容，World 仍是事实权威来源。</summary>
public sealed record NarrativeWorldReferenceNode : NarrativeNode
{
    /// <summary>获取被引用的 World Anchor 标识。</summary>
    public required Guid WorldAnchorId { get; init; }
}
