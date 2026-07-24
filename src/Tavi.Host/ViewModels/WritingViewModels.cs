namespace Tavi.Host.ViewModels;

/// <summary>表示手稿中的一个段落。</summary>
/// <param name="Id">段落稳定标识。</param>
/// <param name="Text">段落完整正文，允许为空。</param>
public sealed record ManuscriptParagraphViewModel(Guid Id, string Text);

/// <summary>表示可供编辑或只读展示的完整手稿。</summary>
/// <param name="Id">手稿稳定标识。</param>
/// <param name="StateId">手稿内容版本。</param>
/// <param name="Title">手稿名称。</param>
/// <param name="Status">Editing 或 Archived。</param>
/// <param name="Paragraphs">按阅读顺序排列的段落。</param>
/// <param name="CreatedAtUtc">UTC 创建时间。</param>
/// <param name="UpdatedAtUtc">UTC 最近修改时间。</param>
public sealed record ManuscriptViewModel(Guid Id, Guid StateId, string Title, string Status, IReadOnlyList<ManuscriptParagraphViewModel> Paragraphs, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>表示手稿库中的一项只读摘要。</summary>
/// <param name="Id">手稿标识。</param>
/// <param name="Title">手稿名称。</param>
/// <param name="Status">Editing 或 Archived。</param>
/// <param name="ParagraphCount">段落数量。</param>
/// <param name="Preview">经过截断的正文预览。</param>
/// <param name="CreatedAtUtc">UTC 创建时间。</param>
/// <param name="UpdatedAtUtc">UTC 最近修改时间。</param>
public sealed record ManuscriptSummaryViewModel(Guid Id, string Title, string Status, int ParagraphCount, string Preview, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>表示活动手稿编辑器的完整状态。</summary>
/// <param name="Manuscript">包含暂存投影的活动手稿；当前空闲时为空。</param>
/// <param name="StagingRevision">暂存区 revision。</param>
/// <param name="IsDirty">是否存在尚未持久化的已提交修改。</param>
/// <param name="CanUndo">是否可以撤销。</param>
/// <param name="CanRedo">是否可以重做。</param>
/// <param name="StagedChangeCount">尚未提交的暂存修改数量。</param>
/// <param name="AutoSaveError">最近一次自动保存错误；没有错误时为空。</param>
public sealed record WritingSnapshotViewModel(ManuscriptViewModel? Manuscript, long StagingRevision, bool IsDirty, bool CanUndo, bool CanRedo, int StagedChangeCount, string? AutoSaveError);

/// <summary>表示手稿库和唯一活动编辑器的聚合页面状态。</summary>
/// <param name="Manuscripts">全部活动与归档手稿摘要。</param>
/// <param name="Session">当前 Writing 编辑器状态。</param>
public sealed record WritingWorkspaceViewModel(IReadOnlyList<ManuscriptSummaryViewModel> Manuscripts, WritingSnapshotViewModel Session);

/// <summary>表示创建空白活动手稿的请求。</summary>
/// <param name="Title">新手稿名称。</param>
public sealed record CreateManuscriptRequest(string Title);

/// <summary>表示修改手稿名称的请求。</summary>
/// <param name="Title">新名称。</param>
/// <param name="ExpectedStateId">活动手稿修改使用的预期状态；修改归档标题时可以为空。</param>
public sealed record RenameManuscriptRequest(string Title, Guid? ExpectedStateId = null);

/// <summary>表示在指定位置添加空白或已有正文段落的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的活动手稿状态。</param>
/// <param name="Index">插入位置，范围为零到当前段落数量。</param>
/// <param name="Text">初始正文，默认允许为空。</param>
public sealed record InsertParagraphRequest(Guid ExpectedStateId, int Index, string Text = "");

/// <summary>表示替换一个段落完整正文的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的活动手稿状态。</param>
/// <param name="Text">新的完整段落正文。</param>
public sealed record UpdateParagraphRequest(Guid ExpectedStateId, string Text);

/// <summary>表示依赖当前手稿状态的操作请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的活动手稿状态。</param>
public sealed record WritingStateRequest(Guid ExpectedStateId);
