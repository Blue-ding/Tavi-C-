namespace Tavi.Runtime;

/// <summary>将 Scenario Runtime 事件扇出到当前进程内订阅者。</summary>
public sealed class ScenarioEventBroker
{
    private readonly RuntimeEventStream<ScenarioRuntimeEvent> _stream = new();

    public void Publish(ScenarioRuntimeEvent runtimeEvent) =>
        _stream.Publish(runtimeEvent);

    public IAsyncEnumerable<ScenarioRuntimeEvent> SubscribeAsync(
        CancellationToken cancellationToken) =>
        _stream.SubscribeAsync(cancellationToken);
}
