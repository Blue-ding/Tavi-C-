using Tavi.Domain.Scenario;
using Tavi.Extensibility;

namespace Tavi.Application.Scenario;

/// <summary>提供通过 ScenarioSession 同步和事务边界推进当前 Scenario 的命令。</summary>
public sealed class ScenarioCommands
{
    private readonly ScenarioSession _session;

    internal ScenarioCommands(ScenarioSession session) => _session = session;

    /// <summary>在完整候选状态通过 Module 语义校验后原子提交 EARS 操作；Scene 生命周期必须使用专用方法。</summary>
    public ScenarioCommitResult Apply(ScenarioChangeSet changeSet, Guid expectedStateId)
        => _session.Apply(changeSet, expectedStateId);

    /// <summary>从当前可用的 SceneDefinition 实例化一个允许不完整绑定的 Binding Scene。</summary>
    public ScenarioCommitResult CreateScene(SceneDefinition definition, Guid expectedStateId)
        => _session.CreateScene(definition, expectedStateId);

    /// <summary>替换 Binding Scene 的一个槽位绑定；空集合表示保留显式空绑定。</summary>
    public ScenarioCommitResult SetSceneBinding(Guid sceneId, string slotId, IReadOnlyList<Guid> elementIds, Guid expectedStateId)
        => _session.SetSceneBinding(sceneId, slotId, elementIds, expectedStateId);

    /// <summary>清除 Binding Scene 的一个槽位绑定并释放对应 Element。</summary>
    public ScenarioCommitResult ClearSceneBinding(Guid sceneId, string slotId, Guid expectedStateId)
        => _session.ClearSceneBinding(sceneId, slotId, expectedStateId);

    /// <summary>校验完整槽位并冻结 Scene，使其进入 Processing。</summary>
    public ScenarioCommitResult BeginSceneProcessing(Guid sceneId, Guid expectedStateId)
        => _session.BeginSceneProcessing(sceneId, expectedStateId);

    /// <summary>调用 Module 规则结算器，并在局部边界校验通过后原子结算 Scene。</summary>
    public Task<ScenarioCommitResult> SettleSceneByRulesAsync(Guid sceneId, long randomSeed, Guid expectedStateId, CancellationToken cancellationToken = default)
        => _session.SettleSceneByRulesAsync(sceneId, randomSeed, expectedStateId, cancellationToken);

    /// <summary>提交受信外部处理流程产生的局部结果；该入口不调用规则结算器。</summary>
    public ScenarioCommitResult SettleScene(Guid sceneId, SceneSettlementProposal proposal, Guid expectedStateId)
        => _session.SettleScene(sceneId, proposal, expectedStateId);

    /// <summary>删除非 Processing Scene；Binding Scene 删除时释放其 Element。</summary>
    public ScenarioCommitResult RemoveScene(Guid sceneId, Guid expectedStateId)
        => _session.RemoveScene(sceneId, expectedStateId);

    /// <summary>删除全部 Settled Scene；没有待清理 Scene 时返回未变化结果。</summary>
    public ScenarioCommitResult ClearSettledScenes(Guid expectedStateId)
        => _session.ClearSettledScenes(expectedStateId);

    /// <summary>原子恢复最近一次提交前的 Scenario。</summary>
    public ScenarioCommitResult Undo(Guid expectedStateId) => _session.Undo(expectedStateId);

    /// <summary>原子重新应用最近一次撤销的 Scenario 操作。</summary>
    public ScenarioCommitResult Redo(Guid expectedStateId) => _session.Redo(expectedStateId);

    /// <summary>立即保存当前 Scenario 快照。</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => _session.SaveAsync(cancellationToken);
}
