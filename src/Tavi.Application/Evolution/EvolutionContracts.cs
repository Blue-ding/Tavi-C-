using Tavi.Domain.Scenario;
using Tavi.Extensibility;

namespace Tavi.Application.Evolution;

/// <summary>指定 EvolutionSession 当前健康状态。</summary>
public enum EvolutionSessionHealth
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
public sealed class EvolutionSessionChangedEventArgs : EventArgs
{
    /// <summary>创建一次已提交变化的通知数据。</summary>
    public EvolutionSessionChangedEventArgs(ScenarioCommitResult commit)
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

/// <summary>定义 Scenario 查询、Module 调度、Scene 生命周期、提交历史和持久化服务。</summary>
public interface IEvolutionService : IAsyncDisposable
{
    /// <summary>在一次 Scenario 原子提交完成且锁已释放后触发。</summary>
    event EventHandler<EvolutionSessionChangedEventArgs>? Changed;

    /// <summary>获取始终通过 Session 同步边界读取最新状态的查询器。</summary>
    ScenarioQueries Queries { get; }

    /// <summary>获取当前会话健康状态。</summary>
    EvolutionSessionHealth Health { get; }

    /// <summary>获取当前 Scenario StateId。</summary>
    Guid StateId { get; }

    /// <summary>获取 Scenario 是否包含尚未保存的修改。</summary>
    bool IsDirty { get; }

    /// <summary>获取当前是否可以撤销。</summary>
    bool CanUndo { get; }

    /// <summary>获取当前是否可以重做。</summary>
    bool CanRedo { get; }

    /// <summary>获取最近一次后台自动保存异常。</summary>
    Exception? LastAutoSaveException { get; }

    /// <summary>从 Store 初始化 Scenario；存档不存在时创建绑定指定 World StateId 的新 Scenario。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>在完整候选状态通过 Module 语义校验后原子提交操作组。</summary>
    ScenarioCommitResult Apply(ScenarioChangeSet changeSet, Guid expectedStateId);

    /// <summary>获取静态声明与代码 Plugin 动态提供的全部 SceneDefinition。</summary>
    Task<IReadOnlyList<SceneDefinition>> GetSceneDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default);

    /// <summary>校验槽位并从指定 SceneDefinition 创建 Ready Scene。</summary>
    ScenarioCommitResult CreateScene(SceneDefinition definition, IReadOnlyDictionary<string, IReadOnlyList<Guid>> bindings, Guid expectedStateId);

    /// <summary>调用 Module 规则结算器并原子提交 Scene 结果。</summary>
    Task<ScenarioCommitResult> SettleSceneByRulesAsync(Guid sceneId, SceneDefinition definition, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default);

    /// <summary>使 Ready Scene 进入与规则结算分离的 Writing 等待状态。</summary>
    ScenarioCommitResult BeginSceneWriting(Guid sceneId, Guid expectedStateId);

    /// <summary>提交外部 Writing 流程产生且已由玩家确认的结构化结果；该入口不调用规则结算器。</summary>
    ScenarioCommitResult CommitWrittenSceneOutcome(Guid sceneId, ScenarioChangeProposal outcome, Guid expectedStateId);

    /// <summary>原子恢复最近一次提交前的 Scenario。</summary>
    ScenarioCommitResult Undo(Guid expectedStateId);

    /// <summary>原子重新应用最近一次撤销的 Scenario 操作。</summary>
    ScenarioCommitResult Redo(Guid expectedStateId);

    /// <summary>立即保存当前 Scenario 快照。</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>仅在 Scenario 为脏状态时保存当前快照。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
