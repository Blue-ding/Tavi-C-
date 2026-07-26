using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Tavi.Runtime;

public sealed class GuidanceEventBroker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<GuidanceRuntimeEvent>>> _sessions = new();

    public void Publish(GuidanceRuntimeEvent guidanceEvent)
    {
        if (!_sessions.TryGetValue(guidanceEvent.SessionId, out ConcurrentDictionary<Guid, Channel<GuidanceRuntimeEvent>>? subscribers))
            return;
        foreach (Channel<GuidanceRuntimeEvent> channel in subscribers.Values)
            channel.Writer.TryWrite(guidanceEvent);
    }

    public async IAsyncEnumerable<GuidanceRuntimeEvent> SubscribeAsync(Guid sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriptionId = Guid.NewGuid();
        ConcurrentDictionary<Guid, Channel<GuidanceRuntimeEvent>> subscribers = _sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, Channel<GuidanceRuntimeEvent>>());
        Channel<GuidanceRuntimeEvent> channel = Channel.CreateBounded<GuidanceRuntimeEvent>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        subscribers.TryAdd(subscriptionId, channel);
        try
        {
            await foreach (GuidanceRuntimeEvent guidanceEvent in channel.Reader.ReadAllAsync(cancellationToken))
                yield return guidanceEvent;
        }
        finally
        {
            subscribers.TryRemove(subscriptionId, out _);
            channel.Writer.TryComplete();
            if (subscribers.IsEmpty)
                _sessions.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<GuidanceRuntimeEvent>>>(sessionId, subscribers));
        }
    }
}
