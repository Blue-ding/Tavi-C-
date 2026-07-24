namespace Tavi.Domain.Story;

/// <summary>指定手稿处于可继续编辑状态还是已经完成归档。</summary>
public enum ManuscriptStatus
{
    /// <summary>手稿是全局唯一的活动草稿，标题和正文均可修改。</summary>
    Editing,

    /// <summary>手稿正文已经定稿，仅允许修改标题或删除整篇记录。</summary>
    Archived
}

/// <summary>表示手稿中具有稳定标识的一个可独立编辑段落。</summary>
public sealed record ManuscriptParagraph
{
    /// <summary>创建手稿段落；段落正文允许为空，以支持先创建再输入的编辑流程。</summary>
    public ManuscriptParagraph(Guid id, string text)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("段落标识不能为空。", nameof(id));
        Id = id;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>获取段落的稳定标识。</summary>
    public Guid Id { get; }

    /// <summary>获取段落正文；空字符串表示尚未填写的空白段落。</summary>
    public string Text { get; }
}

/// <summary>表示可持久化的完整手稿快照；实例及其段落集合均不可变。</summary>
public sealed record Manuscript
{
    /// <summary>创建经过完整校验的手稿快照。</summary>
    public Manuscript(Guid id, Guid stateId, string title, IEnumerable<ManuscriptParagraph> paragraphs, ManuscriptStatus status, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("手稿标识不能为空。", nameof(id));
        if (stateId == Guid.Empty)
            throw new ArgumentException("手稿状态标识不能为空。", nameof(stateId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ManuscriptParagraph[] materialized = paragraphs.ToArray();
        if (materialized.Any(paragraph => paragraph is null))
            throw new ArgumentException("段落集合不能包含空项。", nameof(paragraphs));
        if (materialized.Select(paragraph => paragraph.Id).Distinct().Count() != materialized.Length)
            throw new ArgumentException("同一篇手稿中的段落标识必须唯一。", nameof(paragraphs));
        if (updatedAtUtc < createdAtUtc)
            throw new ArgumentException("手稿更新时间不能早于创建时间。", nameof(updatedAtUtc));
        Id = id;
        StateId = stateId;
        Title = title.Trim();
        Paragraphs = Array.AsReadOnly(materialized);
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>获取手稿的长期稳定标识。</summary>
    public Guid Id { get; }

    /// <summary>获取手稿内容版本；每次实际提交、撤销、重做、重命名或归档都会生成新值。</summary>
    public Guid StateId { get; }

    /// <summary>获取手稿名称。</summary>
    public string Title { get; }

    /// <summary>获取按阅读顺序排列的不可变段落集合。</summary>
    public IReadOnlyList<ManuscriptParagraph> Paragraphs { get; }

    /// <summary>获取手稿当前生命周期状态。</summary>
    public ManuscriptStatus Status { get; }

    /// <summary>获取手稿创建时间，统一使用 UTC。</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>获取手稿最近一次实际修改时间，统一使用 UTC。</summary>
    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>创建一篇不含段落的活动手稿。</summary>
    public static Manuscript Create(string title, DateTimeOffset? now = null)
    {
        DateTimeOffset createdAt = now ?? DateTimeOffset.UtcNow;
        return new Manuscript(Guid.NewGuid(), Guid.NewGuid(), title, [], ManuscriptStatus.Editing, createdAt, createdAt);
    }
}
