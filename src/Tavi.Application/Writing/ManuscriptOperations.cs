using Tavi.Domain.Story;

namespace Tavi.Application.Writing;

/// <summary>指定手稿修改的来源，以便未来区分玩家输入和生成结果。</summary>
public enum WritingChangeSource
{
    /// <summary>修改由玩家直接发起。</summary>
    Player,

    /// <summary>修改由未来的叙事生成流程提出。</summary>
    Generation
}

/// <summary>表示可以暂存、提交并进入撤销历史的一项不可变手稿操作。</summary>
public abstract record ManuscriptOperation;

/// <summary>在指定位置插入一个具有预分配稳定标识的段落。</summary>
/// <param name="ParagraphId">新段落稳定标识，必须非空且不能与已有段落重复。</param>
/// <param name="Index">插入位置，范围为零到当前段落数量。</param>
/// <param name="Text">初始正文，允许为空。</param>
public sealed record InsertParagraphOperation(Guid ParagraphId, int Index, string Text = "") : ManuscriptOperation;

/// <summary>替换指定段落的完整正文。</summary>
/// <param name="ParagraphId">目标段落标识。</param>
/// <param name="Text">新的完整正文，允许为空但不能为 null。</param>
public sealed record UpdateParagraphOperation(Guid ParagraphId, string Text) : ManuscriptOperation;

/// <summary>删除指定段落。</summary>
/// <param name="ParagraphId">目标段落标识。</param>
public sealed record RemoveParagraphOperation(Guid ParagraphId) : ManuscriptOperation;

/// <summary>修改活动手稿名称。</summary>
/// <param name="Title">去除首尾空白后使用的新名称，不能留空。</param>
public sealed record RenameManuscriptOperation(string Title) : ManuscriptOperation;

/// <summary>把一个已解决 Beat 的稳定段落幂等追加到活动手稿。</summary>
internal sealed record PublishBeatOperation(Guid PerformanceId, Guid BeatId, IReadOnlyList<ManuscriptParagraph> Paragraphs) : ManuscriptOperation;

/// <summary>表示暂存日志中的一项手稿修改。</summary>
public sealed record WritingStagedChange
{
    /// <summary>获取暂存项标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取修改来源。</summary>
    public required WritingChangeSource Source { get; init; }

    /// <summary>获取不可变手稿操作。</summary>
    public required ManuscriptOperation Operation { get; init; }
}

internal static class ManuscriptEditor
{
    internal static Manuscript Apply(Manuscript source, IEnumerable<ManuscriptOperation> operations, bool preserveState = false, ManuscriptStatus? status = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operations);
        if (source.Status == ManuscriptStatus.Archived && status is null)
            throw WritingException.ArchivedImmutable(source.Id);
        string title = source.Title;
        var paragraphs = source.Paragraphs.ToList();
        var publications = source.BeatPublications.ToList();
        bool changed = false;
        foreach (ManuscriptOperation operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            switch (operation)
            {
                case InsertParagraphOperation insert:
                    if (insert.ParagraphId == Guid.Empty || paragraphs.Any(paragraph => paragraph.Id == insert.ParagraphId))
                        throw WritingException.ParagraphInvalid("新段落标识无效或已经存在。");
                    if (insert.Index < 0 || insert.Index > paragraphs.Count)
                        throw WritingException.ParagraphInvalid("新段落位置超出正文范围。");
                    paragraphs.Insert(insert.Index, new ManuscriptParagraph(insert.ParagraphId, insert.Text ?? throw new ArgumentNullException(nameof(insert.Text))));
                    changed = true;
                    break;
                case UpdateParagraphOperation update:
                    int updateIndex = paragraphs.FindIndex(paragraph => paragraph.Id == update.ParagraphId);
                    if (updateIndex < 0)
                        throw WritingException.ParagraphInvalid($"不存在段落 {update.ParagraphId}。");
                    string text = update.Text ?? throw new ArgumentNullException(nameof(update.Text));
                    if (paragraphs[updateIndex].Text != text)
                    {
                        paragraphs[updateIndex] = new ManuscriptParagraph(update.ParagraphId, text);
                        changed = true;
                    }
                    break;
                case RemoveParagraphOperation remove:
                    int removeIndex = paragraphs.FindIndex(paragraph => paragraph.Id == remove.ParagraphId);
                    if (removeIndex < 0)
                        throw WritingException.ParagraphInvalid($"不存在段落 {remove.ParagraphId}。");
                    paragraphs.RemoveAt(removeIndex);
                    changed = true;
                    break;
                case RenameManuscriptOperation rename:
                    ArgumentException.ThrowIfNullOrWhiteSpace(rename.Title);
                    string normalized = rename.Title.Trim();
                    if (title != normalized)
                    {
                        title = normalized;
                        changed = true;
                    }
                    break;
                case PublishBeatOperation publish:
                    if (publish.PerformanceId == Guid.Empty || publish.BeatId == Guid.Empty)
                        throw WritingException.StagingInvalid("Performance 与 Beat 标识不能为空。");
                    if (publications.Any(value => value.PerformanceId == publish.PerformanceId && value.BeatId == publish.BeatId))
                        break;
                    ManuscriptParagraph[] generated = publish.Paragraphs?.Select(value => new ManuscriptParagraph(value.Id, value.Text)).ToArray()
                        ?? throw new ArgumentNullException(nameof(publish.Paragraphs));
                    if (generated.Select(value => value.Id).Distinct().Count() != generated.Length ||
                        generated.Any(value => paragraphs.Any(existing => existing.Id == value.Id)))
                        throw WritingException.ParagraphInvalid("Beat 产生了重复或已存在的段落标识。");
                    paragraphs.AddRange(generated);
                    publications.Add(new ManuscriptBeatPublication(publish.PerformanceId, publish.BeatId, generated.Select(value => value.Id)));
                    changed = true;
                    break;
                default:
                    throw new ArgumentException($"不支持的手稿操作 {operation.GetType().Name}。", nameof(operations));
            }
        }
        ManuscriptStatus nextStatus = status ?? source.Status;
        if (!changed && nextStatus == source.Status)
            return source;
        return new Manuscript(source.Id, preserveState ? source.StateId : Guid.NewGuid(), title, paragraphs, nextStatus, source.CreatedAtUtc, preserveState ? source.UpdatedAtUtc : DateTimeOffset.UtcNow, publications);
    }

    internal static Manuscript RebaseContent(Manuscript content, ManuscriptStatus? status = null) => new(content.Id, Guid.NewGuid(), content.Title, content.Paragraphs, status ?? content.Status, content.CreatedAtUtc, DateTimeOffset.UtcNow, content.BeatPublications);
}
