namespace Tavi.Domain.Performance;

/// <summary>指定一次 Performance 是否仍可演绎或已经结束。</summary>
public enum PerformanceStatus
{
    Active,
    Completed,
    Abandoned
}

/// <summary>指定 Beat 从绑定到正文发布的功能生命周期。</summary>
public enum BeatState
{
    Binding,
    Processing,
    Resolved,
    Published
}

/// <summary>表示由 Module 提供并可实例化为 Beat 的稳定定义键。</summary>
public readonly record struct BeatDefinitionType
{
    public BeatDefinitionType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int separator = value.IndexOf(':');
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            separator <= 0 || separator == value.Length - 1)
            throw new ArgumentException("BeatDefinition 类型键必须符合 namespace:name 约定且不能包含首尾空白。", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>保存 Beat 创建时冻结的槽位结构要求。</summary>
public sealed record BeatSlotSpecification
{
    public BeatSlotSpecification(string id, string name, string description, int minimum, int? maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        if (minimum < 0 || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Beat 槽位基数范围无效。");
        Id = id;
        Name = name;
        Description = description;
        Minimum = minimum;
        Maximum = maximum;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int Minimum { get; }
    public int? Maximum { get; }
}

/// <summary>表示 Beat 槽位与 Performance Element 的绑定。</summary>
public sealed record BeatSlotBinding
{
    public BeatSlotBinding(string slotId, IEnumerable<Guid> elementIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(elementIds);
        Guid[] copied = elementIds.ToArray();
        if (copied.Any(id => id == Guid.Empty) || copied.Distinct().Count() != copied.Length)
            throw new ArgumentException("Beat 槽位绑定不能包含空或重复的 Element 标识。", nameof(elementIds));
        SlotId = slotId;
        ElementIds = Array.AsReadOnly(copied);
    }

    public string SlotId { get; }
    public IReadOnlyList<Guid> ElementIds { get; }
}

/// <summary>表示 Beat 希望按给定顺序追加到 Manuscript 的一个稳定段落。</summary>
public sealed record BeatParagraph
{
    public BeatParagraph(Guid id, string text)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Beat 段落标识不能为空。", nameof(id));
        ArgumentNullException.ThrowIfNull(text);
        Id = id;
        Text = text;
    }

    public Guid Id { get; }
    public string Text { get; }
}

/// <summary>记录 Beat 正文已经幂等发布到指定 Manuscript 状态。</summary>
public sealed record BeatPublicationReceipt
{
    public BeatPublicationReceipt(Guid manuscriptId, Guid manuscriptStateId)
    {
        if (manuscriptId == Guid.Empty)
            throw new ArgumentException("Manuscript 标识不能为空。", nameof(manuscriptId));
        if (manuscriptStateId == Guid.Empty)
            throw new ArgumentException("Manuscript StateId 不能为空。", nameof(manuscriptStateId));
        ManuscriptId = manuscriptId;
        ManuscriptStateId = manuscriptStateId;
    }

    public Guid ManuscriptId { get; }
    public Guid ManuscriptStateId { get; }
}
