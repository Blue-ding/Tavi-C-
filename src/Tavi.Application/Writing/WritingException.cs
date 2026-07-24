namespace Tavi.Application.Writing;

/// <summary>定义 Writing 功能可跨层稳定识别的错误码。</summary>
public static class WritingErrorCodes
{
    /// <summary>尝试创建手稿时已经存在活动手稿。</summary>
    public const string ActiveExists = "TAVI.WRITING.SESSION.ACTIVE_EXISTS";

    /// <summary>操作要求活动手稿，但当前没有正在编辑的手稿。</summary>
    public const string NoActive = "TAVI.WRITING.SESSION.NO_ACTIVE";

    /// <summary>调用方观察到的手稿版本已经过期。</summary>
    public const string StateConflict = "TAVI.WRITING.STATE.CONFLICT";

    /// <summary>目标归档手稿不存在。</summary>
    public const string NotFound = "TAVI.WRITING.MANUSCRIPT.NOT_FOUND";

    /// <summary>归档正文不可修改。</summary>
    public const string ArchivedImmutable = "TAVI.WRITING.MANUSCRIPT.ARCHIVED_IMMUTABLE";

    /// <summary>段落不存在或段落位置无效。</summary>
    public const string ParagraphInvalid = "TAVI.WRITING.PARAGRAPH.INVALID";

    /// <summary>暂存选择不存在或无法提交。</summary>
    public const string StagingInvalid = "TAVI.WRITING.STAGING.INVALID";
}

/// <summary>表示 Writing 会话、手稿或段落操作违反稳定业务约定。</summary>
public sealed class WritingException : TaviException
{
    /// <summary>创建具有稳定错误契约的 Writing 异常。</summary>
    public WritingException(string errorCode, TaviErrorCategory category, string message, string operation, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, category, message, operation, false, details, innerException) { }

    internal static WritingException ActiveExists(Guid id) => new(WritingErrorCodes.ActiveExists, TaviErrorCategory.Conflict, "已有一篇正在编辑的手稿；归档后才能开始新的手稿。", "Create", new Dictionary<string, string> { ["ManuscriptId"] = id.ToString() });
    internal static WritingException NoActive(string operation) => new(WritingErrorCodes.NoActive, TaviErrorCategory.InvalidState, "当前没有正在编辑的手稿。", operation);
    internal static WritingException Conflict(Guid expected, Guid actual) => new(WritingErrorCodes.StateConflict, TaviErrorCategory.Conflict, "手稿已被其它操作修改，请刷新后重试。", "Commit", new Dictionary<string, string> { ["ExpectedStateId"] = expected.ToString(), ["ActualStateId"] = actual.ToString() });
    internal static WritingException NotFound(Guid id) => new(WritingErrorCodes.NotFound, TaviErrorCategory.NotFound, $"不存在手稿 {id}。", "Read", new Dictionary<string, string> { ["ManuscriptId"] = id.ToString() });
    internal static WritingException ArchivedImmutable(Guid id) => new(WritingErrorCodes.ArchivedImmutable, TaviErrorCategory.InvalidState, "归档手稿的正文不可修改。", "Edit", new Dictionary<string, string> { ["ManuscriptId"] = id.ToString() });
    internal static WritingException ParagraphInvalid(string message) => new(WritingErrorCodes.ParagraphInvalid, TaviErrorCategory.Validation, message, "EditParagraph");
    internal static WritingException StagingInvalid(string message) => new(WritingErrorCodes.StagingInvalid, TaviErrorCategory.Validation, message, "CommitStaged");
}
