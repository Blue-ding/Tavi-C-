using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Tavi.Runtime;

/// <summary>为 Runtime 模块提供一致的有界事件扇出实现。</summary>
internal sealed class RuntimeEventStream<TEvent> where TEvent : class
{
    private readonly ConcurrentDictionary<Guid, Channel<TEvent>> _subscribers = new();
    private readonly int _capacity;

    internal RuntimeEventStream(int capacity = 64)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal void Publish(TEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        foreach (Channel<TEvent> channel in _subscribers.Values)
            channel.Writer.TryWrite(runtimeEvent);
    }

    internal async IAsyncEnumerable<TEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        Guid subscriptionId = Guid.NewGuid();
        Channel<TEvent> channel = Channel.CreateBounded<TEvent>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        _subscribers.TryAdd(subscriptionId, channel);
        try
        {
            await foreach (TEvent runtimeEvent in channel.Reader.ReadAllAsync(cancellationToken))
                yield return runtimeEvent;
        }
        finally
        {
            _subscribers.TryRemove(subscriptionId, out _);
            channel.Writer.TryComplete();
        }
    }
}
