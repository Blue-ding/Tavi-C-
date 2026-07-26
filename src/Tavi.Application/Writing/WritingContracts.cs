using Tavi.Domain.Story;
using Tavi.Domain.Performance;

namespace Tavi.Application.Writing;

/// <summary>表示手稿库中的一项只读摘要。</summary>
/// <param name="Id">手稿稳定标识。</param>
/// <param name="Title">手稿名称。</param>
/// <param name="Status">活动或归档状态。</param>
/// <param name="ParagraphCount">段落数量。</param>
/// <param name="Preview">经过截断的纯文本预览。</param>
/// <param name="CreatedAtUtc">UTC 创建时间。</param>
/// <param name="UpdatedAtUtc">UTC 最近修改时间。</param>
public sealed record ManuscriptSummary(Guid Id, string Title, ManuscriptStatus Status, int ParagraphCount, string Preview, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>表示当前 Writing 工作区及其暂存投影的完整不可变状态。</summary>
public sealed record WritingSnapshot
{
    /// <summary>获取当前活动手稿的暂存投影；没有活动手稿时为空。</summary>
    public Manuscript? Manuscript { get; init; }

    /// <summary>获取当前暂存区 revision。</summary>
    public long StagingRevision { get; init; }

    /// <summary>获取活动手稿是否包含尚未持久化的已提交修改。</summary>
    public bool IsDirty { get; init; }

    /// <summary>获取是否可以撤销最近一次已提交修改。</summary>
    public bool CanUndo { get; init; }

    /// <summary>获取是否可以重做最近一次撤销。</summary>
    public bool CanRedo { get; init; }

    /// <summary>获取按追加顺序排列的暂存修改。</summary>
    public IReadOnlyList<WritingStagedChange> StagedChanges { get; init; } = [];

    /// <summary>获取最近一次后台自动保存异常；未失败时为空。</summary>
    public Exception? LastAutoSaveException { get; init; }
}

/// <summary>表示一次手稿提交结果。</summary>
/// <param name="CommitId">实际发生修改时生成的提交标识；未改变时为空。</param>
/// <param name="PreviousStateId">提交前手稿状态标识。</param>
/// <param name="StateId">提交后手稿状态标识。</param>
/// <param name="Changed">提交是否实际改变手稿。</param>
public sealed record WritingCommitResult(Guid CommitId, Guid PreviousStateId, Guid StateId, bool Changed);

/// <summary>描述 Beat 正文的幂等发布结果。</summary>
public sealed record BeatPublicationResult(Guid ManuscriptId, Guid ManuscriptStateId, bool AlreadyPublished);

/// <summary>定义单活动手稿、归档库、编辑历史和持久化的 Application 服务。</summary>
public interface IWritingService : IAsyncDisposable
{
    /// <summary>从持久化存储恢复唯一活动手稿；同一服务实例只能初始化一次。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>获取当前活动手稿及暂存投影的权威快照。</summary>
    WritingSnapshot GetSnapshot();

    /// <summary>列出全部手稿摘要，包含当前活动手稿和归档手稿。</summary>
    Task<IReadOnlyList<ManuscriptSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>读取指定手稿；活动手稿返回当前已提交内容，归档手稿返回只读正文。</summary>
    Task<Manuscript> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>创建并立即持久化全局唯一的空白活动手稿。</summary>
    Task<WritingSnapshot> CreateAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>将不可变手稿操作追加到暂存日志，不修改已提交正文。</summary>
    Guid Stage(ManuscriptOperation operation, WritingChangeSource source = WritingChangeSource.Player);

    /// <summary>删除指定暂存项；不存在时返回 false。</summary>
    bool DeleteStaged(Guid changeId);

    /// <summary>以乐观并发条件原子提交选中的暂存操作。</summary>
    WritingCommitResult CommitStaged(IEnumerable<Guid> changeIds, Guid expectedStateId);

    /// <summary>原子提交一项操作，供不需要展示暂存区的段落编辑界面使用。</summary>
    WritingCommitResult Apply(ManuscriptOperation operation, Guid expectedStateId, WritingChangeSource source = WritingChangeSource.Player);

    /// <summary>把已解决 Beat 的稳定段落幂等追加到活动 Manuscript；重复 BeatId 返回原发布结果。</summary>
    BeatPublicationResult PublishBeat(Guid performanceId, Guid beatId, IReadOnlyList<BeatParagraph> paragraphs, Guid expectedStateId);

    /// <summary>撤销最近一次已提交修改；撤销本身会生成新的状态标识。</summary>
    WritingCommitResult Undo(Guid expectedStateId);

    /// <summary>重做最近一次撤销；重做本身会生成新的状态标识。</summary>
    WritingCommitResult Redo(Guid expectedStateId);

    /// <summary>原子持久化当前已提交手稿；保存不会结束编辑状态。</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>提交全部暂存修改并原子归档当前手稿；成功后释放活动编辑席位。</summary>
    Task<Manuscript> ArchiveAsync(Guid expectedStateId, CancellationToken cancellationToken = default);

    /// <summary>修改归档手稿名称；归档正文保持不变。</summary>
    Task<Manuscript> RenameArchivedAsync(Guid manuscriptId, string title, CancellationToken cancellationToken = default);

    /// <summary>永久删除归档手稿；活动手稿不能通过此操作删除。</summary>
    Task DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default);
}

/// <summary>定义活动手稿和归档库所需的持久化端口。</summary>
public interface IManuscriptStore
{
    /// <summary>读取唯一活动手稿；不存在时返回空。</summary>
    Task<Manuscript?> LoadActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>列出全部归档手稿。</summary>
    Task<IReadOnlyList<Manuscript>> ListArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>读取指定归档手稿；不存在时返回空。</summary>
    Task<Manuscript?> LoadArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>原子保存活动手稿，并拒绝覆盖不同标识的活动手稿。</summary>
    Task SaveActiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default);

    /// <summary>原子写入归档手稿并释放匹配的活动手稿记录。</summary>
    Task ArchiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default);

    /// <summary>原子替换指定归档手稿。</summary>
    Task SaveArchivedAsync(Manuscript manuscript, CancellationToken cancellationToken = default);

    /// <summary>永久删除指定归档手稿；不存在时返回 false。</summary>
    Task<bool> DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default);
}
