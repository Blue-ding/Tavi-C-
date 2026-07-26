using Tavi.Domain.Scenario;

namespace Tavi.Application.Scenario;

/// <summary>指定 ScenarioSession 当前健康状态。</summary>
public enum ScenarioSessionHealth
{
    /// <summary>会话可以正常查询、修改和保存。</summary>
    Healthy,

    /// <summary>会话因事务恢复失败而无法继续可靠工作。</summary>
    Faulted
}

/// <summary>描述一次 Scenario 原子提交的结果。</summary>
/// <param name="CommitId">提交标识；未变化时为空 Guid。</param><param name="PreviousStateId">提交前 StateId。</param><param name="StateId">提交后 StateId。</param><param name="ChangeSet">实际变化及逆操作；未变化时为 null。</param>
public sealed record ScenarioCommitResult(Guid CommitId, Guid PreviousStateId, Guid StateId, AppliedScenarioChangeSet? ChangeSet)
{
    /// <summary>获取本次提交是否实际改变了 Scenario。</summary>
    public bool Changed => ChangeSet is not null;

    /// <summary>创建未发生变化的提交结果。</summary>
    public static ScenarioCommitResult Unchanged(Guid stateId) => new(Guid.Empty, stateId, stateId, null);
}

/// <summary>提供 Scenario 原子提交后的独立通知数据。</summary>
public sealed class ScenarioSessionChangedEventArgs : EventArgs
{
    /// <summary>创建一次已提交变化的通知数据。</summary>
    public ScenarioSessionChangedEventArgs(ScenarioCommitResult commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (!commit.Changed)
            throw new ArgumentException("未变化提交不能产生 Changed 事件。", nameof(commit));
        Commit = commit;
    }

    /// <summary>获取本次 Scenario 提交结果。</summary>
    public ScenarioCommitResult Commit { get; }
}

/// <summary>定义 Scenario 快照的异步持久化边界。</summary>
public interface IScenarioStore
{
    /// <summary>从指定存档槽读取独立 Scenario 快照；存档不存在时返回 null。</summary>
    Task<ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default);

    /// <summary>将独立 Scenario 快照保存到指定存档槽。</summary>
    Task SaveAsync(string slot, ScenarioSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>定义调用方可查询和推进的活动 Scenario 工作区。</summary>
public interface IScenarioWorkspace
{
    /// <summary>获取始终通过 Session 同步边界读取最新状态的查询器。</summary>
    ScenarioQueries Queries { get; }

    /// <summary>获取始终通过 Session 同步和事务边界执行的命令器。</summary>
    ScenarioCommands Commands { get; }

    /// <summary>获取当前会话健康状态。</summary>
    ScenarioSessionHealth Health { get; }

    /// <summary>获取当前 Scenario StateId。</summary>
    Guid StateId { get; }

    /// <summary>获取 Scenario 是否包含尚未保存的修改。</summary>
    bool IsDirty { get; }

    /// <summary>获取当前是否可以撤销。</summary>
    bool CanUndo { get; }

    /// <summary>获取当前是否可以重做。</summary>
    bool CanRedo { get; }

    /// <summary>获取最近一次防抖保存异常。</summary>
    Exception? LastAutoSaveException { get; }
}

/// <summary>定义 Runtime 管理 Scenario Session 初始化、通知和最终刷新的生命周期角色。</summary>
public interface IScenarioSessionLifecycle : IAsyncDisposable
{
    /// <summary>在一次 Scenario 原子提交完成且锁已释放后触发。</summary>
    event EventHandler<ScenarioSessionChangedEventArgs>? Changed;

    /// <summary>从 Store 初始化 Scenario；存档不存在时创建绑定指定 World StateId 的新 Scenario。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>仅在 Scenario 为脏状态时保存当前快照。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
