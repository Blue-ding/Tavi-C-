namespace Tavi.Runtime;

/// <summary>将世界会话事件扇出到每一个当前进程内订阅者。</summary>
public sealed class WorldEventBroker
{
    private readonly RuntimeEventStream<WorldRuntimeEvent> _stream = new();

    /// <summary>向所有当前订阅者发布世界事件。</summary>
    public void Publish(WorldRuntimeEvent worldEvent)
    {
        _stream.Publish(worldEvent);
    }

    /// <summary>创建一个事件订阅，并在枚举结束后自动注销订阅者。</summary>
    public IAsyncEnumerable<WorldRuntimeEvent> SubscribeAsync(
        CancellationToken cancellationToken) =>
        _stream.SubscribeAsync(cancellationToken);
}
