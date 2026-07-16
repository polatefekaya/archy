using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Sessions.ArchitectureSessions;

namespace Archy.UnitTests.Features.Integrations.Codex.RecordHookEvents;

public sealed class HookEventPublisherTests
{
    [Fact]
    public async Task PublishFansOutTheSameDurableEventToEveryActiveSubscriber()
    {
        var publisher = new HookEventPublisher();
        using var first = publisher.Subscribe();
        using var second = publisher.Subscribe();
        var publication = new HookEventPublication(
            new SessionEvent("event:1", "session:1", 2, SessionEventKind.ValidationCompleted, 4, null, null, "{}", DateTimeOffset.UnixEpoch),
            new HookValidationEvent(1, 4, true, ["ARCHY002:test"]));

        publisher.Publish(publication);

        Assert.Equal(publication, await first.Reader.ReadAsync());
        Assert.Equal(publication, await second.Reader.ReadAsync());
    }

    [Fact]
    public async Task DisposeCompletesOnlyTheDisposedSubscription()
    {
        var publisher = new HookEventPublisher();
        var disposed = publisher.Subscribe();
        using var active = publisher.Subscribe();
        disposed.Dispose();

        publisher.Publish(Publication());

        Assert.False(await disposed.Reader.WaitToReadAsync());
        Assert.Equal("event:1", (await active.Reader.ReadAsync()).SessionEvent.EventId);
    }

    private static HookEventPublication Publication() => new(
        new SessionEvent("event:1", "session:1", 2, SessionEventKind.ValidationCompleted, 4, null, null, "{}", DateTimeOffset.UnixEpoch),
        new HookValidationEvent(1, 4, false, []));
}
