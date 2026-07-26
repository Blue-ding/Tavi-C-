using Tavi.Application.Scenario;
using Tavi.Domain.Performance;
using Tavi.Extensibility;

namespace Tavi.Runtime;

/// <summary>串行协调 Processing Scene 启动 Performance，以及 Performance 结果回写 Scenario。</summary>
public sealed class ScenarioPerformanceRuntime
{
    private readonly ScenarioRuntime _scenario;
    private readonly PerformanceRuntime _performance;
    private readonly ScenarioPerformanceCoordinator _coordinator = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScenarioPerformanceRuntime(ScenarioRuntime scenario, PerformanceRuntime performance)
    {
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    /// <summary>从指定的 Performance-capable Processing Scene 创建唯一活动 Performance。</summary>
    public async Task<PerformanceSnapshot> StartPerformanceAsync(
        Guid sceneId,
        Guid expectedScenarioStateId,
        long randomSeed,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _scenario.ExecuteAsync(async scenario =>
            {
                SceneContextView context = _coordinator.PrepareStart(scenario, sceneId, expectedScenarioStateId);
                return await _performance.StartPerformanceAsync(context, randomSeed, cancellationToken);
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>把 Performance 结算提案提交到来源 Scene，并仅在提交成功后结束 Performance。</summary>
    public async Task<ScenarioPerformanceCompletion> CompletePerformanceAsync(
        Guid expectedScenarioStateId,
        Guid expectedPerformanceStateId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _scenario.ExecuteAsync(
                scenario => _performance.ExecuteAsync(
                    performance => _coordinator.CompleteAsync(
                        scenario,
                        performance,
                        expectedScenarioStateId,
                        expectedPerformanceStateId,
                        cancellationToken),
                    cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
