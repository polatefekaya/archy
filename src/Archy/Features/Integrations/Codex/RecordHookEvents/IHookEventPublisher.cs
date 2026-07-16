using System.Threading.Channels;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

public interface IHookEventPublisher
{
    HookEventSubscription Subscribe();
    void Publish(SessionEventPublication publication);
}

public sealed class HookEventSubscription(ChannelReader<SessionEventPublication> reader, Action unsubscribe) : IDisposable
{
    public ChannelReader<SessionEventPublication> Reader { get; } = reader;
    public void Dispose() => unsubscribe();
}
