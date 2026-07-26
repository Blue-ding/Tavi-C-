using Tavi.Domain.Story;

namespace Tavi.Application.Writing;

/// <summary>提供通过 WritingSession 同步和事务边界编辑活动手稿及归档库的命令。</summary>
public sealed class WritingCommands
{
    private readonly WritingSession _session;

    internal WritingCommands(WritingSession session) => _session = session;

    /// <summary>创建并立即持久化全局唯一的空白活动手稿。</summary>
    public Task<WritingSnapshot> CreateAsync(string title, CancellationToken cancellationToken = default)
        => _session.CreateAsync(title, cancellationToken);

    /// <summary>将不可变手稿操作追加到暂存日志，不修改已提交正文。</summary>
    public Guid Stage(ManuscriptOperation operation, WritingChangeSource source = WritingChangeSource.Player)
        => _session.Stage(operation, source);

    /// <summary>删除指定暂存项；不存在时返回 false。</summary>
    public bool DeleteStaged(Guid changeId) => _session.DeleteStaged(changeId);

    /// <summary>以乐观并发条件原子提交选中的暂存操作。</summary>
    public WritingCommitResult CommitStaged(IEnumerable<Guid> changeIds, Guid expectedStateId)
        => _session.CommitStaged(changeIds, expectedStateId);

    /// <summary>原子提交一项操作，供不需要展示暂存区的段落编辑界面使用。</summary>
    public WritingCommitResult Apply(ManuscriptOperation operation, Guid expectedStateId, WritingChangeSource source = WritingChangeSource.Player)
        => _session.Apply(operation, expectedStateId, source);

    /// <summary>撤销最近一次已提交修改。</summary>
    public WritingCommitResult Undo(Guid expectedStateId) => _session.Undo(expectedStateId);

    /// <summary>重做最近一次撤销。</summary>
    public WritingCommitResult Redo(Guid expectedStateId) => _session.Redo(expectedStateId);

    /// <summary>原子持久化当前已提交手稿；保存不会结束编辑状态。</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => _session.SaveAsync(cancellationToken);

    /// <summary>提交全部暂存修改并原子归档当前手稿。</summary>
    public Task<Manuscript> ArchiveAsync(Guid expectedStateId, CancellationToken cancellationToken = default)
        => _session.ArchiveAsync(expectedStateId, cancellationToken);

    /// <summary>修改归档手稿名称；归档正文保持不变。</summary>
    public Task<Manuscript> RenameArchivedAsync(Guid manuscriptId, string title, CancellationToken cancellationToken = default)
        => _session.RenameArchivedAsync(manuscriptId, title, cancellationToken);

    /// <summary>永久删除归档手稿；活动手稿不能通过此操作删除。</summary>
    public Task DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
        => _session.DeleteArchivedAsync(manuscriptId, cancellationToken);
}
