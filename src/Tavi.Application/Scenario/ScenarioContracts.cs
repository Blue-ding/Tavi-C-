using Tavi.Domain.Scenario;
using Tavi.Extensibility;

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

/// <summary>定义 Scenario 查询、Module Definition、Scene 生命周期、提交历史和持久化服务。</summary>
public interface IScenarioService : IAsyncDisposable
{
    /// <summary>在一次 Scenario 原子提交完成且锁已释放后触发。</summary>
    event EventHandler<ScenarioSessionChangedEventArgs>? Changed;

    /// <summary>获取始终通过 Session 同步边界读取最新状态的查询器。</summary>
    ScenarioQueries Queries { get; }

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

    /// <summary>从 Store 初始化 Scenario；存档不存在时创建绑定指定 World StateId 的新 Scenario。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>在完整候选状态通过 Module 语义校验后原子提交 EARS 操作；Scene 生命周期必须使用专用方法。</summary>
    ScenarioCommitResult Apply(ScenarioChangeSet changeSet, Guid expectedStateId);

    /// <summary>获取静态声明与代码 Plugin 动态提供的全部 SceneDefinition。</summary>
    Task<IReadOnlyList<SceneDefinition>> GetSceneDefinitionsAsync(long randomSeed, CancellationToken cancellationToken = default);

    /// <summary>从当前可用的 SceneDefinition 实例化一个允许不完整绑定的 Binding Scene。</summary>
    ScenarioCommitResult CreateScene(SceneDefinition definition, Guid expectedStateId);

    /// <summary>替换 Binding Scene 的一个槽位绑定；空集合表示保留显式空绑定。</summary>
    ScenarioCommitResult SetSceneBinding(Guid sceneId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId);

    /// <summary>清除 Binding Scene 的一个槽位绑定并释放对应 Element。</summary>
    ScenarioCommitResult ClearSceneBinding(Guid sceneId, string slotId, Guid expectedStateId);

    /// <summary>校验完整槽位并冻结 Scene，使其进入 Processing。</summary>
    ScenarioCommitResult BeginSceneProcessing(Guid sceneId, Guid expectedStateId);

    /// <summary>获取供未来 Writing 或其他受信处理器使用的冻结 Scene 局部上下文。</summary>
    SceneContextView GetProcessingContext(Guid sceneId);

    /// <summary>调用 Module 规则结算器，并在局部边界校验通过后原子结算 Scene。</summary>
    Task<ScenarioCommitResult> SettleSceneByRulesAsync(Guid sceneId, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default);

    /// <summary>提交受信外部处理流程产生的局部结果；该入口不调用规则结算器。</summary>
    ScenarioCommitResult SettleScene(Guid sceneId, SceneSettlementProposal proposal, Guid expectedStateId);

    /// <summary>删除非 Processing Scene；Binding Scene 删除时释放其 Element。</summary>
    ScenarioCommitResult RemoveScene(Guid sceneId, Guid expectedStateId);

    /// <summary>删除全部 Settled Scene；没有待清理 Scene 时返回未变化结果。</summary>
    ScenarioCommitResult ClearSettledScenes(Guid expectedStateId);

    /// <summary>原子恢复最近一次提交前的 Scenario。</summary>
    ScenarioCommitResult Undo(Guid expectedStateId);

    /// <summary>原子重新应用最近一次撤销的 Scenario 操作。</summary>
    ScenarioCommitResult Redo(Guid expectedStateId);

    /// <summary>立即保存当前 Scenario 快照。</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>仅在 Scenario 为脏状态时保存当前快照。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
