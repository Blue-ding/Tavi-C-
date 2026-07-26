using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>定义调用方可查询和修改的活动 World 工作区。</summary>
public interface IWorldWorkspace
{
    /// <summary>获取始终通过工作区同步边界读取最新状态的查询工具。</summary>
    WorldQueries Queries { get; }

    /// <summary>获取始终通过工作区同步和事务边界执行的命令工具。</summary>
    WorldCommands Commands { get; }

    /// <summary>获取当前工作区健康状态。</summary>
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

    /// <summary>创建包含全部暂存项及临时 World 投影的不可变快照。</summary>
    WorldStagingSnapshot CreateStagingSnapshot();
}

/// <summary>定义 Runtime 管理 World Session 启停、通知和最终刷新的生命周期角色。</summary>
public interface IWorldSessionLifecycle : IAsyncDisposable
{
    /// <summary>在一个原子操作组成功提交后触发；事件处理器在写锁释放后执行。</summary>
    event EventHandler<WorldSessionChangedEventArgs>? Changed;

    /// <summary>在脏状态或保存状态发生变化后触发。</summary>
    event EventHandler<WorldSessionStateChangedEventArgs>? StateChanged;

    /// <summary>从存档槽加载 World；存档不存在时创建新 World。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>当前 World 包含未保存修改时立即写入存档。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
