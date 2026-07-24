namespace Tavi.Domain.Narrative;

/// <summary>表示 NarrativeGraph 中一条有向带属性边。图允许同一对节点存在多条边，以表达同一参与者在同一 Beat 中承担多个角色。</summary>
public sealed record NarrativeLink
{
    /// <summary>获取边标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取起点节点标识。</summary>
    public required Guid SourceId { get; init; }

    /// <summary>获取终点节点标识。</summary>
    public required Guid TargetId { get; init; }

    /// <summary>获取边类型；核心和模块类型共享相同的命名空间键机制。</summary>
    public required NarrativeTypeKey Type { get; init; }

    /// <summary>获取边强度，约定为零到一之间的有限值。</summary>
    public double Strength { get; init; } = 1;

    /// <summary>获取该关系是否属于选择时不可违反的硬约束。</summary>
    public bool IsHardConstraint { get; init; }

    /// <summary>获取按稳定特征键索引的模块扩展特征。</summary>
    public IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> Features { get; init; } = new Dictionary<NarrativeFeatureKey, NarrativeFeatureValue>();
}
