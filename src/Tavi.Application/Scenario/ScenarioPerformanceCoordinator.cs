using Tavi.Application.Performance;
using Tavi.Domain.Performance;
using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Tavi.Utilities.Concurrency;

namespace Tavi.Application.Scenario;

/// <summary>描述一次 Performance 结果回写 Scenario 并结束 Performance 的受控编排结果。</summary>
public sealed record ScenarioPerformanceCompletion(
    Guid SceneId,
    ScenarioCommitResult ScenarioCommit,
    PerformanceCommitResult PerformanceCommit);

/// <summary>集中执行 Scenario 与 Performance 之间的来源校验、上下文冻结和结算顺序。</summary>
public sealed class ScenarioPerformanceCoordinator
{
    /// <summary>从允许 Performance 结算的 Processing Scene 获取冻结启动上下文。</summary>
    public SceneContextView PrepareStart(
        IScenarioWorkspace scenario,
        Guid sceneId,
        Guid expectedScenarioStateId)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return scenario.Queries.GetPerformanceContext(sceneId, expectedScenarioStateId);
    }

    /// <summary>将全部已发布 Beat 产生的提案提交到来源 Scene，随后结束对应 Performance。</summary>
    public async Task<ScenarioPerformanceCompletion> CompleteAsync(
        IScenarioWorkspace scenario,
        IPerformanceWorkspace performance,
        Guid expectedScenarioStateId,
        Guid expectedPerformanceStateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(performance);
        if (scenario.StateId != expectedScenarioStateId)
            throw Conflict(nameof(CompleteAsync), $"Scenario 状态冲突：期望 {expectedScenarioStateId}，实际 {scenario.StateId}。");
        if (performance.StateId != expectedPerformanceStateId)
            throw new OptimisticConcurrencyConflictException(expectedPerformanceStateId, performance.StateId);

        PerformanceSnapshot snapshot = performance.Queries.CreateSnapshot();
        if (snapshot.Status != PerformanceStatus.Active)
            throw Invalid(nameof(CompleteAsync), $"Performance {snapshot.PerformanceId} 当前状态 {snapshot.Status}，不能结算 Scene。");
        if (snapshot.SourceScenarioStateId != expectedScenarioStateId)
            throw Conflict(nameof(CompleteAsync), $"Performance {snapshot.PerformanceId} 来源 Scenario 状态为 {snapshot.SourceScenarioStateId}，当前期望为 {expectedScenarioStateId}。");

        SceneContextView context = scenario.Queries.GetPerformanceContext(snapshot.SourceScene.Id, expectedScenarioStateId);
        if (context.Scene.DefinitionId.Value != snapshot.SourceScene.DefinitionId ||
            context.Scene.Module.Value != snapshot.SourceScene.ModuleId ||
            context.Scene.ModuleVersion.Value != snapshot.SourceScene.ModuleVersion)
            throw Conflict(nameof(CompleteAsync), $"Performance {snapshot.PerformanceId} 的来源 Scene 身份与当前 Scenario 不一致。");

        SceneSettlementProposal proposal = await performance.Commands.PrepareSettlementAsync(cancellationToken);
        ScenarioCommitResult scenarioCommit = scenario.Commands.SettleScene(snapshot.SourceScene.Id, proposal, expectedScenarioStateId);
        PerformanceCommitResult performanceCommit = performance.Commands.Complete(expectedPerformanceStateId);
        return new ScenarioPerformanceCompletion(snapshot.SourceScene.Id, scenarioCommit, performanceCommit);
    }

    private static ScenarioApplicationException Conflict(string operation, string message)
        => new(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, operation, message);

    private static ScenarioApplicationException Invalid(string operation, string message)
        => new(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, operation, message);
}
