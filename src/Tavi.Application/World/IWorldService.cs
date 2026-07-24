using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>定义当前 World 的查询、暂存、提交、历史和持久化 Application 服务。</summary>
public interface IWorldService : IAsyncDisposable
{
    /// <summary>在一个原子操作组成功提交后触发；事件处理器在写锁释放后执行。</summary>
    event EventHandler<WorldSessionChangedEventArgs>? Changed;

    /// <summary>在脏状态或保存状态发生变化后触发。</summary>
    event EventHandler<WorldSessionStateChangedEventArgs>? StateChanged;

    /// <summary>获取始终通过服务同步边界读取最新状态的查询工具。</summary>
    WorldQueries Queries { get; }

    /// <summary>获取当前服务健康状态。</summary>
    WorldSessionHealth Health { get; }

    /// <summary>获取当前 World 状态标识。</summary>
    Guid StateId { get; }

    /// <summary>获取当前 World 是否包含尚未保存的修改。</summary>
    bool IsDirty { get; }

    /// <summary>获取当前是否存在可撤销的已提交操作组。</summary>
    bool CanUndo { get; }

    /// <summary>获取当前是否存在可重做的已提交操作组。</summary>
    bool CanRedo { get; }

    /// <summary>获取当前暂存区 revision。</summary>
    long StagingRevision { get; }

    /// <summary>获取最近一次后台自动保存异常。</summary>
    Exception? LastAutoSaveException { get; }

    /// <summary>从存档槽加载 World；存档不存在时创建新 World。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>以预期状态标识为乐观并发条件原子提交操作组。</summary>
    WorldCommitResult Apply(WorldChangeSet changeSet, Guid expectedStateId);

    /// <summary>将一项不可变 World 操作追加到暂存日志。</summary>
    Guid Stage(WorldOperation operation, WorldStagedChangeSource source = WorldStagedChangeSource.Player);

    /// <summary>将一个不可变 World 操作组作为单项记录追加到暂存日志。</summary>
    Guid Stage(WorldChangeSet changeSet, WorldStagedChangeSource source = WorldStagedChangeSource.Player);

    /// <summary>将一组不可变 World 操作按顺序追加到暂存日志。</summary>
    IReadOnlyList<Guid> Stage(IEnumerable<WorldOperation> operations, WorldStagedChangeSource source);

    /// <summary>创建包含全部暂存项及临时 World 投影的不可变快照。</summary>
    WorldStagingSnapshot CreateStagingSnapshot();

    /// <summary>删除指定暂存日志项；不存在时返回 false。</summary>
    bool DeleteStaged(Guid changeId);

    /// <summary>删除全部无效暂存项并返回删除数量。</summary>
    int DeleteInvalidStaged();

    /// <summary>原子提交选中的有效暂存项并在成功后消费它们。</summary>
    WorldStagingCommitResult CommitStaged(IEnumerable<Guid> selectedChangeIds, Guid expectedStateId);

    /// <summary>原子应用最近一次提交的反向操作。</summary>
    WorldCommitResult Undo(Guid expectedStateId);

    /// <summary>原子重新应用最近一次撤销的正向操作。</summary>
    WorldCommitResult Redo(Guid expectedStateId);

    /// <summary>立即保存当前 World，无论当前是否为脏状态。</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>当前 World 包含未保存修改时立即写入存档。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
