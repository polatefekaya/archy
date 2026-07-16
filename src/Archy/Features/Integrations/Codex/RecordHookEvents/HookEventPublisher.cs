using System.Threading.Channels;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

/// <summary>In-process fan-out for durable hook events. Slow live consumers can replay missed items from session storage.</summary>
public sealed class HookEventPublisher : IHookEventPublisher
{
    private const int SubscriberCapacity = 256;
    private readonly object gate = new();
    private readonly HashSet<Channel<SessionEventPublication>> subscribers = [];

    public HookEventSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<SessionEventPublication>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        lock (gate)
        {
            subscribers.Add(channel);
        }

        return new HookEventSubscription(channel.Reader, () => Unsubscribe(channel));
    }

    public void Publish(SessionEventPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (gate)
        {
            foreach (var subscriber in subscribers)
            {
                _ = subscriber.Writer.TryWrite(publication);
            }
        }
    }

    private void Unsubscribe(Channel<SessionEventPublication> channel)
    {
        lock (gate)
        {
            if (subscribers.Remove(channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
