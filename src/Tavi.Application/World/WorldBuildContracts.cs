using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>定义所有调用方都可安全持有的 World 构筑只读视图。</summary>
public interface IWorldBuildView
{
    /// <summary>获取始终通过构筑会话同步边界读取最新已提交 World 的查询工具。</summary>
    WorldQueries Queries { get; }

    /// <summary>获取当前构筑会话健康状态。</summary>
    WorldBuildHealth Health { get; }

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

    /// <summary>创建包含全部提案、冲突及候选 World 投影的不可变快照。</summary>
    WorldStagingSnapshot CreateStagingSnapshot();
}

/// <summary>定义只能提出 World 修改、不能审批或提交真实 World 的构筑权限。</summary>
public interface IWorldBuildContributor : IWorldBuildView
{
    /// <summary>把单个不可变操作作为一个提案写入构筑日志。</summary>
    Guid Stage(WorldOperation operation, WorldStagedChangeSource source, Guid expectedWorldStateId);

    /// <summary>把不可变操作组作为一个原子提案写入构筑日志。</summary>
    Guid Stage(WorldChangeSet changeSet, WorldStagedChangeSource source, Guid expectedWorldStateId);

    /// <summary>把多个操作分别作为提案按顺序写入构筑日志。</summary>
    IReadOnlyList<Guid> Stage(IEnumerable<WorldOperation> operations, WorldStagedChangeSource source, Guid expectedWorldStateId);
}

/// <summary>定义代表玩家审批提案、修改历史和保存真实 World 的控制权限。</summary>
public interface IWorldBuildController : IWorldBuildView
{
    bool DeleteStaged(Guid changeId);
    int DeleteInvalidStaged();
    WorldStagingCommitResult CommitStaged(IEnumerable<Guid> selectedChangeIds, Guid expectedStateId);
    WorldCommitResult Undo(Guid expectedStateId);
    WorldCommitResult Redo(Guid expectedStateId);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>组合一个受信任调用方可持有的完整 World 构筑工作区。</summary>
public interface IWorldBuildWorkspace : IWorldBuildContributor, IWorldBuildController
{
}

/// <summary>定义 Runtime 管理 WorldBuildSession 启停、通知和最终刷新的生命周期角色。</summary>
public interface IWorldBuildSessionLifecycle : IAsyncDisposable
{
    /// <summary>在一个原子操作组成功提交后触发；事件处理器在写锁释放后执行。</summary>
    event EventHandler<WorldBuildChangedEventArgs>? Changed;

    /// <summary>在脏状态或保存状态发生变化后触发。</summary>
    event EventHandler<WorldBuildStateChangedEventArgs>? StateChanged;

    /// <summary>从存档槽加载 World；存档不存在时创建新 World。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>当前 World 包含未保存修改时立即写入存档。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
