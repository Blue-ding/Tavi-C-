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

/// <summary>提供 Writing 原子提交后的独立通知数据。</summary>
public sealed class WritingSessionChangedEventArgs : EventArgs
{
    public WritingSessionChangedEventArgs(
        Guid manuscriptId,
        WritingCommitResult commit,
        string operation)
    {
        if (manuscriptId == Guid.Empty)
            throw new ArgumentException("Manuscript 标识不能为空。", nameof(manuscriptId));
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (!commit.Changed)
            throw new ArgumentException("未变化提交不能产生 Changed 事件。", nameof(commit));
        ManuscriptId = manuscriptId;
        Commit = commit;
        Operation = operation;
    }

    public Guid ManuscriptId { get; }
    public WritingCommitResult Commit { get; }
    public string Operation { get; }
}

/// <summary>描述 Beat 正文的幂等发布结果。</summary>
public sealed record BeatPublicationResult(Guid ManuscriptId, Guid ManuscriptStateId, bool AlreadyPublished);

/// <summary>定义 Performance 将已解决 Beat 幂等发布到当前活动手稿所需的最小能力。</summary>
public interface IBeatPublisher
{
    /// <summary>把已解决 Beat 的稳定段落幂等追加到活动 Manuscript。</summary>
    BeatPublicationResult PublishBeat(Guid performanceId, Guid beatId, IReadOnlyList<BeatParagraph> paragraphs, Guid expectedStateId);
}

/// <summary>定义调用方可查询和编辑唯一活动手稿及归档库的 Writing 工作区。</summary>
public interface IWritingWorkspace
{
    /// <summary>获取始终通过 Session 同步边界读取最新状态的查询器。</summary>
    WritingQueries Queries { get; }

    /// <summary>获取始终通过 Session 同步和事务边界执行的命令器。</summary>
    WritingCommands Commands { get; }
}

/// <summary>定义 Runtime 管理 Writing Session 恢复和最终刷新的生命周期角色。</summary>
public interface IWritingSessionLifecycle : IAsyncDisposable
{
    /// <summary>在一次原子正文提交完成且锁已释放后触发。</summary>
    event EventHandler<WritingSessionChangedEventArgs>? Changed;

    /// <summary>在脏状态或保存状态发生变化后触发。</summary>
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>从持久化存储恢复唯一活动手稿；同一 Session 实例只能初始化一次。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>活动手稿包含未保存修改时立即写入持久化存储。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
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
