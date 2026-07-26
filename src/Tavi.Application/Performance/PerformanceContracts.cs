using Tavi.Domain.Performance;

namespace Tavi.Application.Performance;

public enum PerformanceSessionHealth
{
    Healthy,
    Faulted
}

public sealed record PerformanceCommitResult(Guid CommitId, Guid PreviousStateId, Guid StateId, AppliedPerformanceChangeSet? ChangeSet)
{
    public bool Changed => ChangeSet is not null;
    public static PerformanceCommitResult Unchanged(Guid stateId) => new(Guid.Empty, stateId, stateId, null);
}

public sealed class PerformanceSessionChangedEventArgs : EventArgs
{
    public PerformanceSessionChangedEventArgs(Guid performanceId, PerformanceCommitResult commit)
    {
        if (performanceId == Guid.Empty)
            throw new ArgumentException("Performance 标识不能为空。", nameof(performanceId));
        ArgumentNullException.ThrowIfNull(commit);
        if (!commit.Changed)
            throw new ArgumentException("未变化提交不能产生 Changed 事件。", nameof(commit));
        PerformanceId = performanceId;
        Commit = commit;
    }

    public Guid PerformanceId { get; }
    public PerformanceCommitResult Commit { get; }
}

/// <summary>定义唯一活动 Performance 快照及历史记录的持久化端口。</summary>
public interface IPerformanceStore
{
    Task<PerformanceSnapshot?> LoadActiveAsync(CancellationToken cancellationToken = default);
    Task SaveActiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ArchiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceSnapshot>> ListArchivedAsync(CancellationToken cancellationToken = default);
    Task<PerformanceSnapshot?> LoadArchivedAsync(Guid performanceId, CancellationToken cancellationToken = default);
}

/// <summary>定义调用方可查询和推进的唯一活动 Performance 工作区。</summary>
public interface IPerformanceWorkspace
{
    Guid Id { get; }
    Guid StateId { get; }
    PerformanceStatus Status { get; }
    PerformanceSessionHealth Health { get; }
    bool IsDirty { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    Exception? LastAutoSaveException { get; }
    PerformanceQueries Queries { get; }
    PerformanceCommands Commands { get; }
}

/// <summary>定义 Runtime 管理 Performance Session 恢复、通知和最终刷新的生命周期角色。</summary>
public interface IPerformanceSessionLifecycle : IAsyncDisposable
{
    event EventHandler<PerformanceSessionChangedEventArgs>? Changed;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
}
