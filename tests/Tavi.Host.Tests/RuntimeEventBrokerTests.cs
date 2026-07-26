using Tavi.Runtime;
using Xunit;

namespace Tavi.Host.Tests;

public sealed class RuntimeEventBrokerTests
{
    [Fact]
    public async Task ScenarioPerformanceAndWritingBrokersExposeSameEventShape()
    {
        var scenario = new ScenarioEventBroker();
        var performance = new PerformanceEventBroker();
        var writing = new WritingEventBroker();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Task<ScenarioRuntimeEvent> scenarioEvent =
            ReadOneAsync(scenario.SubscribeAsync(timeout.Token));
        Task<PerformanceRuntimeEvent> performanceEvent =
            ReadOneAsync(performance.SubscribeAsync(timeout.Token));
        Task<WritingRuntimeEvent> writingEvent =
            ReadOneAsync(writing.SubscribeAsync(timeout.Token));
        await Task.Yield();

        Guid stateId = Guid.NewGuid();
        Guid commitId = Guid.NewGuid();
        scenario.Publish(new ScenarioRuntimeEvent(
            "scenario.changed", stateId, true, commitId, "Apply", null));
        performance.Publish(new PerformanceRuntimeEvent(
            "performance.changed", stateId, true, commitId, "Apply", null));
        writing.Publish(new WritingRuntimeEvent(
            "writing.changed", stateId, true, commitId, "Apply", null));

        AssertEquivalent(await scenarioEvent);
        AssertEquivalent(await performanceEvent);
        AssertEquivalent(await writingEvent);

        void AssertEquivalent(dynamic runtimeEvent)
        {
            Assert.Equal(stateId, runtimeEvent.StateId);
            Assert.True(runtimeEvent.IsDirty);
            Assert.Equal(commitId, runtimeEvent.CommitId);
            Assert.Equal("Apply", runtimeEvent.Operation);
            Assert.Null(runtimeEvent.Error);
        }
    }

    private static async Task<T> ReadOneAsync<T>(
        IAsyncEnumerable<T> events)
    {
        await foreach (T runtimeEvent in events)
            return runtimeEvent;
        throw new InvalidOperationException("事件流在产生事件前结束。");
    }
}
