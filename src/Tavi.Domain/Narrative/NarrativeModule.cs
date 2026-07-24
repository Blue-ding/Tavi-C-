namespace Tavi.Domain.Narrative;

/// <summary>指定模块对一个 Beat 给出的硬选择约束。</summary>
public enum NarrativeConstraintKind
{
    /// <summary>模块允许该 Beat 参与选择；它不会覆盖其他模块给出的 Block。</summary>
    Allow,

    /// <summary>模块阻止该 Beat 参与本次选择。</summary>
    Block,

    /// <summary>选择其他依赖项时必须同时选择该 Beat。</summary>
    Require
}

/// <summary>表示模块对 Beat 的一个命名评分分量；未来 Profile 负责组合分量，Kernel 不解释分量名称。</summary>
public sealed record NarrativeScoreContribution
{
    /// <summary>获取被评分的 Beat 标识。</summary>
    public required Guid BeatId { get; init; }

    /// <summary>获取模块命名空间内的评分分量键。</summary>
    public required NarrativeFeatureKey Component { get; init; }

    /// <summary>获取有限评分值；该值不限定为零到一，以允许惩罚项。</summary>
    public required double Value { get; init; }
}

/// <summary>表示模块对一个 Beat 给出的结构化选择约束。</summary>
public sealed record NarrativeConstraint
{
    /// <summary>获取约束标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取受约束的 Beat 标识。</summary>
    public required Guid BeatId { get; init; }

    /// <summary>获取约束种类。</summary>
    public required NarrativeConstraintKind Kind { get; init; }

    /// <summary>获取可供诊断和作者理解的理由；Kernel 不解析该文本。</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>表示 World、Scene 或其他受控边界反馈给 Narrative 模块的结构化信号。内核只路由类型和特征，不解析说明文本。</summary>
public sealed record NarrativeSignal
{
    /// <summary>获取信号标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取由产生信号的设施注册的稳定类型。</summary>
    public required NarrativeTypeKey Type { get; init; }

    /// <summary>获取与该信号直接相关的 Narrative 节点标识。</summary>
    public IReadOnlySet<Guid> RelatedNodeIds { get; init; } = new HashSet<Guid>();

    /// <summary>获取供模块程序化处理的结构化特征。</summary>
    public IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> Features { get; init; } = new Dictionary<NarrativeFeatureKey, NarrativeFeatureValue>();

    /// <summary>获取供诊断或 LLM 使用的说明；Kernel 不解析该文本。</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>表示模块希望所选子图生成 Scene 时满足的一项要求。要求保持结构化类型，但具体解释权属于对应的 Scene 设施。</summary>
public sealed record NarrativeSceneRequirement
{
    /// <summary>获取要求标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取产生该要求的 Beat 标识。</summary>
    public required Guid BeatId { get; init; }

    /// <summary>获取由模块注册的要求类型。</summary>
    public required NarrativeTypeKey Type { get; init; }

    /// <summary>获取该要求是否不可由 Scene 设施忽略。</summary>
    public bool IsRequired { get; init; }

    /// <summary>获取供 Scene 设施或 LLM 使用的说明；Kernel 不解析该文本。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>获取供对应 Scene 设施处理的结构化特征。</summary>
    public IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> Features { get; init; } = new Dictionary<NarrativeFeatureKey, NarrativeFeatureValue>();
}

/// <summary>描述模块建议 Kernel 在合并和统一校验后执行的一项图操作。模块只能返回操作数据，不能取得或修改 NarrativeGraph 内部状态。</summary>
public abstract record NarrativeGraphOperation
{
    private NarrativeGraphOperation() { }

    /// <summary>建议添加节点。</summary>
    public sealed record AddNode(NarrativeNode Node) : NarrativeGraphOperation;

    /// <summary>建议以同标识的新值替换节点。</summary>
    public sealed record ReplaceNode(NarrativeNode Node) : NarrativeGraphOperation;

    /// <summary>建议删除节点及其关联边。</summary>
    public sealed record RemoveNode(Guid NodeId) : NarrativeGraphOperation;

    /// <summary>建议添加有向边。</summary>
    public sealed record AddLink(NarrativeLink Link) : NarrativeGraphOperation;

    /// <summary>建议删除有向边。</summary>
    public sealed record RemoveLink(Guid LinkId) : NarrativeGraphOperation;
}

/// <summary>表示一个 Narrative 模块对当前不可变快照的完整评估结果。Kernel 未来将负责冲突消解、预算、随机选择与原子提交。</summary>
public sealed record NarrativeModuleProposal
{
    /// <summary>获取产生该提案的模块标识。</summary>
    public required NarrativeTypeKey Module { get; init; }

    /// <summary>获取模块建议的图操作。</summary>
    public IReadOnlyList<NarrativeGraphOperation> Operations { get; init; } = [];

    /// <summary>获取模块提供的评分分量。</summary>
    public IReadOnlyList<NarrativeScoreContribution> Scores { get; init; } = [];

    /// <summary>获取模块提供的硬选择约束。</summary>
    public IReadOnlyList<NarrativeConstraint> Constraints { get; init; } = [];

    /// <summary>获取模块希望 Scene 设施满足的结构化要求。</summary>
    public IReadOnlyList<NarrativeSceneRequirement> SceneRequirements { get; init; } = [];
}

/// <summary>表示模块评估一次 Tick 时可读取的全部信息；仅暴露独立快照是为了阻止模块产生顺序相关的共享状态修改。</summary>
public sealed record NarrativeModuleContext
{
    /// <summary>获取当前 NarrativeGraph 独立快照。</summary>
    public required NarrativeGraphSnapshot Graph { get; init; }

    /// <summary>获取本次评估 Tick。</summary>
    public required long Tick { get; init; }

    /// <summary>获取由 Kernel 分配给本次模块评估的可复现随机种子。</summary>
    public required long RandomSeed { get; init; }

    /// <summary>获取自上次 Tick 后由 World、Scene 或其他受控边界产生的结构化反馈。</summary>
    public IReadOnlyList<NarrativeSignal> Signals { get; init; } = [];
}

/// <summary>定义 Narrative 有界工作集与随机选择的通用参数；这些限制属于 Profile 配置，不属于任何特定故事模块。</summary>
public sealed record NarrativeProfilePolicy
{
    /// <summary>获取允许保留的最大 Beat 数。</summary>
    public required int MaximumBeats { get; init; }

    /// <summary>获取允许保留的最大 World 引用节点数。</summary>
    public required int MaximumWorldReferences { get; init; }

    /// <summary>获取允许保留的最大边数。</summary>
    public required int MaximumLinks { get; init; }

    /// <summary>获取参与带权随机选择的最高分候选数量。</summary>
    public required int SelectionPoolSize { get; init; }

    /// <summary>获取带权随机选择温度；零表示未来 Kernel 应执行严格贪心选择。</summary>
    public required double SelectionTemperature { get; init; }

    /// <summary>获取从中心 Beat 构造 SceneSeed 时允许遍历的最大邻域深度。</summary>
    public required int SceneNeighborhoodDepth { get; init; }
}

/// <summary>定义一种可组合的故事生成设施。模块必须是对输入上下文的无副作用评估器，并把全部意图返回为 NarrativeModuleProposal。</summary>
public interface INarrativeModule
{
    /// <summary>获取模块稳定标识；建议使用“模块名:module”。</summary>
    NarrativeTypeKey Id { get; }

    /// <summary>获取模块持久化兼容版本。</summary>
    int Version { get; }

    /// <summary>评估当前 Narrative 状态并返回结构化提案；实现不得保存或修改 Graph 中对象的引用。</summary>
    NarrativeModuleProposal Evaluate(NarrativeModuleContext context);
}

/// <summary>定义组合多个 Narrative 模块时使用的故事 Profile。Profile 负责评分权重和模块优先级，避免把悬疑、情感等策略写入核心图。</summary>
public interface INarrativeProfile
{
    /// <summary>获取 Profile 稳定标识。</summary>
    NarrativeTypeKey Id { get; }

    /// <summary>获取 Profile 持久化兼容版本。</summary>
    int Version { get; }

    /// <summary>获取按确定顺序排列的模块；该顺序只用于稳定合并，不授权模块直接覆盖其他模块状态。</summary>
    IReadOnlyList<INarrativeModule> Modules { get; }

    /// <summary>获取该 Profile 的图预算和通用选择参数。</summary>
    NarrativeProfilePolicy Policy { get; }

    /// <summary>获取指定评分分量在最终选择分数中的权重。</summary>
    double GetScoreWeight(NarrativeFeatureKey component);
}
