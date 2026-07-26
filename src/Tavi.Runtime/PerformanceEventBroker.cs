namespace Tavi.Runtime;

/// <summary>将 Performance Runtime 事件扇出到当前进程内订阅者。</summary>
public sealed class PerformanceEventBroker
{
    private readonly RuntimeEventStream<PerformanceRuntimeEvent> _stream = new();

    public void Publish(PerformanceRuntimeEvent runtimeEvent) =>
        _stream.Publish(runtimeEvent);

    public IAsyncEnumerable<PerformanceRuntimeEvent> SubscribeAsync(
        CancellationToken cancellationToken) =>
        _stream.SubscribeAsync(cancellationToken);
}
