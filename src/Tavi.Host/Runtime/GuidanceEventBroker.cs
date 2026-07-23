using System.Collections.Concurrent;
using System.Threading.Channels;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Runtime;

internal sealed class GuidanceEventBroker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<GuidanceEventViewModel>>> _sessions = new();

    internal void Publish(GuidanceEventViewModel guidanceEvent)
    {
        if (!_sessions.TryGetValue(guidanceEvent.SessionId, out ConcurrentDictionary<Guid, Channel<GuidanceEventViewModel>>? subscribers))
            return;
        foreach (Channel<GuidanceEventViewModel> channel in subscribers.Values)
            channel.Writer.TryWrite(guidanceEvent);
    }

    internal async IAsyncEnumerable<GuidanceEventViewModel> SubscribeAsync(Guid sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriptionId = Guid.NewGuid();
        ConcurrentDictionary<Guid, Channel<GuidanceEventViewModel>> subscribers = _sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, Channel<GuidanceEventViewModel>>());
        Channel<GuidanceEventViewModel> channel = Channel.CreateBounded<GuidanceEventViewModel>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        subscribers.TryAdd(subscriptionId, channel);
        try
        {
            await foreach (GuidanceEventViewModel guidanceEvent in channel.Reader.ReadAllAsync(cancellationToken))
                yield return guidanceEvent;
        }
        finally
        {
            subscribers.TryRemove(subscriptionId, out _);
            channel.Writer.TryComplete();
            if (subscribers.IsEmpty)
                _sessions.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<GuidanceEventViewModel>>>(sessionId, subscribers));
        }
    }
}
