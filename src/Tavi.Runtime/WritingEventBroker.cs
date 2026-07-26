namespace Tavi.Runtime;

/// <summary>将 Writing Runtime 事件扇出到当前进程内订阅者。</summary>
public sealed class WritingEventBroker
{
    private readonly RuntimeEventStream<WritingRuntimeEvent> _stream = new();

    public void Publish(WritingRuntimeEvent runtimeEvent) =>
        _stream.Publish(runtimeEvent);

    public IAsyncEnumerable<WritingRuntimeEvent> SubscribeAsync(
        CancellationToken cancellationToken) =>
        _stream.SubscribeAsync(cancellationToken);
}
