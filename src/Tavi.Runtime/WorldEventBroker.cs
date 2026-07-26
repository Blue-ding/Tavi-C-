using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Tavi.Runtime;

/// <summary>将世界会话事件扇出到每一个当前进程内订阅者。</summary>
public sealed class WorldEventBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<WorldRuntimeEvent>> _subscribers = new();

    /// <summary>向所有当前订阅者发布世界事件。</summary>
    public void Publish(WorldRuntimeEvent worldEvent)
    {
        ArgumentNullException.ThrowIfNull(worldEvent);
        foreach (Channel<WorldRuntimeEvent> channel in _subscribers.Values)
            channel.Writer.TryWrite(worldEvent);
    }

    /// <summary>创建一个事件订阅，并在枚举结束后自动注销订阅者。</summary>
    public async IAsyncEnumerable<WorldRuntimeEvent> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriptionId = Guid.NewGuid();
        Channel<WorldRuntimeEvent> channel = Channel.CreateBounded<WorldRuntimeEvent>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        _subscribers.TryAdd(subscriptionId, channel);
        try
        {
            await foreach (WorldRuntimeEvent worldEvent in channel.Reader.ReadAllAsync(cancellationToken))
                yield return worldEvent;
        }
        finally
        {
            _subscribers.TryRemove(subscriptionId, out _);
            channel.Writer.TryComplete();
        }
    }
}
