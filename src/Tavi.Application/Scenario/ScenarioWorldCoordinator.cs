using Tavi.Application.World;
using Tavi.Domain.Scenario;
using Tavi.Domain.World;

namespace Tavi.Application.Scenario;

/// <summary>描述当前 Scenario 与活动 World 的连接状态。</summary>
public enum ScenarioWorldLinkState
{
    /// <summary>Scenario 仍基于当前 World，且包含尚未完成的 Scene。</summary>
    InProgress,

    /// <summary>Scenario 仍基于当前 World，可以生成回写提案。</summary>
    ReadyToPublish,

    /// <summary>World 构筑日志中存在其他来源的提案；Scenario 仍可回写并由统一日志判定是否冲突。</summary>
    WorldBuildPending,

    /// <summary>当前 Scenario 的演绎结果已经进入 World 暂存区。</summary>
    OutcomeStaged,

    /// <summary>活动 World 已偏离 Scenario 的来源状态，不能直接回写。</summary>
    WorldChanged
}

/// <summary>提供给展示层的 World/Scenario 连接摘要。</summary>
public sealed record ScenarioWorldLink(
    ScenarioWorldLinkState State,
    Guid CurrentWorldStateId,
    Guid SourceWorldStateId,
    Guid ScenarioStateId,
    int BindingScenes,
    int ProcessingScenes,
    int SettledScenes);

/// <summary>描述一次 Scenario 结果写入 World 暂存区的结果。</summary>
public sealed record ScenarioWorldStageResult(
    Guid WorldStateId,
    Guid ScenarioStateId,
    Guid? ChangeId)
{
    /// <summary>获取本次结果是否产生了 World 暂存项。</summary>
    public bool Changed => ChangeId.HasValue;
}

/// <summary>集中执行 World 与 Scenario 之间的状态检查、差异编译和结果暂存。</summary>
public sealed class ScenarioWorldCoordinator
{
    /// <summary>读取当前 World/Scenario 连接状态。</summary>
    public ScenarioWorldLink GetLink(IWorldBuildView world, IScenarioWorkspace scenario)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scenario);
        ScenarioSnapshot snapshot = scenario.Queries.CreateSnapshot();
        WorldStagingSnapshot staging = world.CreateStagingSnapshot();
        (int binding, int processing, int settled) = CountScenes(snapshot);
        ScenarioWorldLinkState state = snapshot.SourceWorldStateId != world.StateId
            ? ScenarioWorldLinkState.WorldChanged
            : staging.Changes.Any(change => change.Source == WorldStagedChangeSource.Scenario)
                ? ScenarioWorldLinkState.OutcomeStaged
                : staging.Changes.Count > 0
                    ? ScenarioWorldLinkState.WorldBuildPending
            : binding > 0 || processing > 0
                ? ScenarioWorldLinkState.InProgress
                : ScenarioWorldLinkState.ReadyToPublish;
        return new ScenarioWorldLink(state, world.StateId, snapshot.SourceWorldStateId, snapshot.Id, binding, processing, settled);
    }

    /// <summary>把已完成 Scenario 相对来源 World 的差异作为一个原子暂存项写入 World。</summary>
    public ScenarioWorldStageResult StageOutcome(
        IWorldBuildView world,
        IWorldBuildContributor contributor,
        IScenarioWorkspace scenario,
        Guid expectedWorldStateId,
        Guid expectedScenarioStateId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(scenario);
        if (world.StateId != expectedWorldStateId)
            throw Conflict(nameof(StageOutcome), $"World 状态冲突：期望 {expectedWorldStateId}，实际 {world.StateId}。");
        if (scenario.StateId != expectedScenarioStateId)
            throw Conflict(nameof(StageOutcome), $"Scenario 状态冲突：期望 {expectedScenarioStateId}，实际 {scenario.StateId}。");
        ScenarioSnapshot scenarioSnapshot = scenario.Queries.CreateSnapshot();
        Scene? unfinished = scenarioSnapshot.Scenes.Values.FirstOrDefault(scene => scene.State is SceneState.Binding or SceneState.Processing);
        if (unfinished is not null)
            throw Invalid(nameof(StageOutcome), $"Scene {unfinished.Id} 当前处于 {unfinished.State}，必须完成或移除后才能回写 World。");
        WorldSnapshot worldSnapshot = world.Queries.CreateSnapshot();
        ScenarioWorldProposal proposal = ScenarioWorldBridge.CreateProposal(worldSnapshot, scenarioSnapshot);
        if (proposal.ChangeSet.IsEmpty)
            return new ScenarioWorldStageResult(worldSnapshot.Id, scenarioSnapshot.Id, null);
        Guid changeId = contributor.Stage(proposal.ChangeSet, WorldStagedChangeSource.Scenario, expectedWorldStateId);
        return new ScenarioWorldStageResult(worldSnapshot.Id, scenarioSnapshot.Id, changeId);
    }

    private static (int Binding, int Processing, int Settled) CountScenes(ScenarioSnapshot snapshot)
        => (
            snapshot.Scenes.Values.Count(scene => scene.State == SceneState.Binding),
            snapshot.Scenes.Values.Count(scene => scene.State == SceneState.Processing),
            snapshot.Scenes.Values.Count(scene => scene.State == SceneState.Settled));

    private static ScenarioApplicationException Conflict(string operation, string message)
        => new(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, message);

    private static ScenarioApplicationException Invalid(string operation, string message)
        => new(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, operation, message);
}
