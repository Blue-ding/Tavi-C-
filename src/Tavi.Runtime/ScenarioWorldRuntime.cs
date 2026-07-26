using Tavi.Application.Scenario;
using Tavi.Domain.Scenario;

namespace Tavi.Runtime;

/// <summary>串行协调活动 World 与 Scenario 的显式分叉和结果回写。</summary>
public sealed class ScenarioWorldRuntime
{
    private readonly WorldRuntime _world;
    private readonly ScenarioRuntime _scenario;
    private readonly ScenarioWorldCoordinator _coordinator = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScenarioWorldRuntime(WorldRuntime world, ScenarioRuntime scenario)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
    }

    /// <summary>读取当前 World 与 Scenario 的连接状态。</summary>
    public async Task<ScenarioWorldLink> GetLinkAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _world.ExecuteAsync(
                world => _scenario.ExecuteAsync(
                    scenario => _coordinator.GetLink(world, scenario),
                    cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>从当前已提交且无暂存项的 World 显式重建空 Scenario。</summary>
    public async Task<ScenarioSnapshot> StartFromWorldAsync(Guid expectedWorldStateId, Guid expectedScenarioStateId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _world.ExecuteAsync(async world =>
            {
                if (world.StateId != expectedWorldStateId)
                    throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.StateConflict, TaviErrorCategory.Conflict, nameof(StartFromWorldAsync), $"World 状态冲突：期望 {expectedWorldStateId}，实际 {world.StateId}。");
                if (world.CreateStagingSnapshot().Changes.Count > 0)
                    throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.InvalidSessionState, TaviErrorCategory.InvalidState, nameof(StartFromWorldAsync), "World 暂存区必须为空，才能开始新的 Scenario。");
                return await _scenario.ReplaceFromWorldAsync(world.Queries.CreateSnapshot(), expectedScenarioStateId, cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>把已完成 Scenario 的结果作为单个原子项写入 World 暂存区。</summary>
    public async Task<ScenarioWorldStageResult> StageOutcomeAsync(Guid expectedWorldStateId, Guid expectedScenarioStateId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _scenario.ExecuteAsync(
                scenario => _world.ExecuteAsync(
                    world => _coordinator.StageOutcome(world, scenario, expectedWorldStateId, expectedScenarioStateId),
                    cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
