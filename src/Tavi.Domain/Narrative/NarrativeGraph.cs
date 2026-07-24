namespace Tavi.Domain.Narrative;

/// <summary>提供对当前 Narrative 有界工作集的只读图查询。首版刻意不公开修改入口：未来 Kernel 必须先合并模块提案并统一验证，模块不能直接改变图。</summary>
public sealed class NarrativeGraph
{
    private readonly NarrativeGraphSnapshot _snapshot;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _outgoingLinkIds;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _incomingLinkIds;
    private readonly IReadOnlyDictionary<Guid, Guid> _nodeIdsByWorldElement;

    private NarrativeGraph(NarrativeGraphSnapshot snapshot)
    {
        _snapshot = CloneSnapshot(snapshot);
        _outgoingLinkIds = _snapshot.Links.Values.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(link => link.Id).ToArray());
        _incomingLinkIds = _snapshot.Links.Values.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(link => link.Id).ToArray());
        _nodeIdsByWorldElement = _snapshot.Nodes.Values.OfType<NarrativeWorldReferenceNode>().ToDictionary(node => node.WorldElementId, node => node.Id);
    }

    /// <summary>校验并创建 NarrativeGraph；校验保证标识非空、边端点存在、数值有限且每个 World Element 最多对应一个引用节点。</summary>
    public static NarrativeGraph Create(NarrativeGraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);
        return new NarrativeGraph(snapshot);
    }

    /// <summary>获取图状态标识。</summary>
    public Guid StateId => _snapshot.StateId;

    /// <summary>获取图所依据的 World 状态标识。</summary>
    public Guid SourceWorldStateId => _snapshot.SourceWorldStateId;

    /// <summary>获取当前演化 Tick。</summary>
    public long Tick => _snapshot.Tick;

    /// <summary>创建当前图的独立快照。</summary>
    public NarrativeGraphSnapshot CreateSnapshot() => CloneSnapshot(_snapshot);

    /// <summary>根据标识获取节点副本。</summary>
    public NarrativeNode GetNode(Guid nodeId) => CloneNode(_snapshot.Nodes.TryGetValue(nodeId, out NarrativeNode? node) ? node : throw new KeyNotFoundException($"未找到 Narrative 节点 {nodeId}。"));

    /// <summary>根据标识获取边副本。</summary>
    public NarrativeLink GetLink(Guid linkId) => CloneLink(_snapshot.Links.TryGetValue(linkId, out NarrativeLink? link) ? link : throw new KeyNotFoundException($"未找到 Narrative 边 {linkId}。"));

    /// <summary>获取指定节点的全部出边副本。</summary>
    public IReadOnlyList<NarrativeLink> GetOutgoingLinks(Guid nodeId)
    {
        EnsureNode(nodeId);
        return _outgoingLinkIds.GetValueOrDefault(nodeId, []).Select(linkId => CloneLink(_snapshot.Links[linkId])).ToArray();
    }

    /// <summary>获取指定节点的全部入边副本。</summary>
    public IReadOnlyList<NarrativeLink> GetIncomingLinks(Guid nodeId)
    {
        EnsureNode(nodeId);
        return _incomingLinkIds.GetValueOrDefault(nodeId, []).Select(linkId => CloneLink(_snapshot.Links[linkId])).ToArray();
    }

    /// <summary>根据 World Element 标识获取唯一的轻量引用节点；当前工作集未引用该 Element 时返回 null。</summary>
    public NarrativeWorldReferenceNode? FindWorldReference(Guid worldElementId)
    {
        if (worldElementId == Guid.Empty)
            throw new ArgumentException("World Element 标识不能为空。", nameof(worldElementId));
        return _nodeIdsByWorldElement.TryGetValue(worldElementId, out Guid nodeId) ? (NarrativeWorldReferenceNode)CloneNode(_snapshot.Nodes[nodeId]) : null;
    }

    private void EnsureNode(Guid nodeId)
    {
        if (!_snapshot.Nodes.ContainsKey(nodeId))
            throw new KeyNotFoundException($"未找到 Narrative 节点 {nodeId}。");
    }

    private static void Validate(NarrativeGraphSnapshot snapshot)
    {
        if (snapshot.StateId == Guid.Empty)
            throw new ArgumentException("NarrativeGraphSnapshot.StateId 不能为空。", nameof(snapshot));
        if (snapshot.SourceWorldStateId == Guid.Empty)
            throw new ArgumentException("NarrativeGraphSnapshot.SourceWorldStateId 不能为空。", nameof(snapshot));
        if (snapshot.Tick < 0)
            throw new ArgumentException("NarrativeGraphSnapshot.Tick 不能小于零。", nameof(snapshot));
        if (snapshot.ProfileVersion <= 0)
            throw new ArgumentException("NarrativeGraphSnapshot.ProfileVersion 必须大于零。", nameof(snapshot));
        if (snapshot.Profile.IsEmpty)
            throw new ArgumentException("NarrativeGraphSnapshot.Profile 不能为空。", nameof(snapshot));
        foreach ((Guid key, NarrativeNode node) in snapshot.Nodes)
        {
            if (node is null)
                throw new ArgumentException($"Narrative 节点字典键 {key} 对应 null。", nameof(snapshot));
            if (key == Guid.Empty || node.Id == Guid.Empty || key != node.Id)
                throw new ArgumentException($"Narrative 节点字典键 {key} 与节点标识不合法或不一致。", nameof(snapshot));
            ValidateNode(node);
            ValidateFeatures(node.Features, $"Narrative 节点 {node.Id}");
        }
        Guid duplicateElementId = snapshot.Nodes.Values.OfType<NarrativeWorldReferenceNode>().GroupBy(node => node.WorldElementId).Where(group => group.Count() > 1).Select(group => group.Key).FirstOrDefault();
        if (duplicateElementId != Guid.Empty)
            throw new ArgumentException($"World Element {duplicateElementId} 在 NarrativeGraph 中存在多个引用节点。", nameof(snapshot));
        foreach ((Guid key, NarrativeLink link) in snapshot.Links)
        {
            if (link is null)
                throw new ArgumentException($"Narrative 边字典键 {key} 对应 null。", nameof(snapshot));
            if (key == Guid.Empty || link.Id == Guid.Empty || key != link.Id)
                throw new ArgumentException($"Narrative 边字典键 {key} 与边标识不合法或不一致。", nameof(snapshot));
            if (!snapshot.Nodes.ContainsKey(link.SourceId) || !snapshot.Nodes.ContainsKey(link.TargetId))
                throw new ArgumentException($"Narrative 边 {link.Id} 的端点不存在。", nameof(snapshot));
            if (link.Type.IsEmpty)
                throw new ArgumentException($"Narrative 边 {link.Id} 的类型不能为空。", nameof(snapshot));
            EnsureUnitValue(link.Strength, $"Narrative 边 {link.Id} 的 Strength");
            ValidateFeatures(link.Features, $"Narrative 边 {link.Id}");
        }
    }

    private static void ValidateNode(NarrativeNode node)
    {
        switch (node)
        {
            case NarrativeBeatNode beat:
                if (!Enum.IsDefined(beat.State) || beat.Age < 0 || beat.Cooldown < 0 || beat.CreatedAtTick < 0 || beat.LastActivatedAtTick < 0)
                    throw new ArgumentException($"Narrative Beat {beat.Id} 的生命周期值不合法。", nameof(node));
                if (beat.EvidenceRelationIds.Any(id => id == Guid.Empty))
                    throw new ArgumentException($"Narrative Beat {beat.Id} 的来源 Relation 标识不能为空。", nameof(node));
                if (beat.EvidenceAspectIds.Any(id => id == Guid.Empty))
                    throw new ArgumentException($"Narrative Beat {beat.Id} 的来源 Aspect 标识不能为空。", nameof(node));
                EnsureUnitValue(beat.Salience, $"Narrative Beat {beat.Id} 的 Salience");
                EnsureUnitValue(beat.Tension, $"Narrative Beat {beat.Id} 的 Tension");
                EnsureUnitValue(beat.Momentum, $"Narrative Beat {beat.Id} 的 Momentum");
                EnsureUnitValue(beat.Novelty, $"Narrative Beat {beat.Id} 的 Novelty");
                break;
            case NarrativeWorldReferenceNode worldReference when worldReference.WorldElementId == Guid.Empty:
                throw new ArgumentException($"Narrative World 引用节点 {worldReference.Id} 的 WorldElementId 不能为空。", nameof(node));
            case NarrativeWorldReferenceNode:
                break;
            default:
                throw new ArgumentException($"不支持的 Narrative 节点类型 {node.GetType().FullName}。", nameof(node));
        }
    }

    private static void EnsureUnitValue(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, value, "Narrative 核心评分必须是零到一之间的有限值。");
    }

    private static void ValidateFeatures(IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> features, string owner)
    {
        if (features is null)
            throw new ArgumentException($"{owner} 的 Features 不能为 null。", nameof(features));
        foreach ((NarrativeFeatureKey key, NarrativeFeatureValue value) in features)
        {
            if (key.IsEmpty || value is null)
                throw new ArgumentException($"{owner} 包含空特征键或 null 特征值。", nameof(features));
        }
    }

    private static NarrativeGraphSnapshot CloneSnapshot(NarrativeGraphSnapshot source) => source with
    {
        Nodes = source.Nodes.ToDictionary(pair => pair.Key, pair => CloneNode(pair.Value)),
        Links = source.Links.ToDictionary(pair => pair.Key, pair => CloneLink(pair.Value))
    };

    private static NarrativeNode CloneNode(NarrativeNode source) => source switch
    {
        NarrativeBeatNode beat => beat with { Features = CloneFeatures(beat.Features), EvidenceRelationIds = beat.EvidenceRelationIds.ToHashSet(), EvidenceAspectIds = beat.EvidenceAspectIds.ToHashSet() },
        NarrativeWorldReferenceNode worldReference => worldReference with { Features = CloneFeatures(worldReference.Features) },
        _ => throw new InvalidOperationException($"不支持的 Narrative 节点类型 {source.GetType().FullName}。")
    };

    private static NarrativeLink CloneLink(NarrativeLink source) => source with { Features = CloneFeatures(source.Features) };

    private static IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> CloneFeatures(IReadOnlyDictionary<NarrativeFeatureKey, NarrativeFeatureValue> source) => source.ToDictionary(pair => pair.Key, pair => pair.Value);
}
