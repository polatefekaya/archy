using System.Text.Json;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Web.StreamHookEvents;

namespace Archy.UnitTests.Features.Web.StreamHookEvents;

public sealed class EventStreamJsonWriterTests
{
    [Fact]
    public void PreservesValidSmallPayloadAsJsonRatherThanAnEscapedString()
    {
        var bytes = EventStreamJsonWriter.Serialize(Event("{\"result\":\"allowed\"}"));
        using var document = JsonDocument.Parse(bytes);

        Assert.Equal("archy.event-stream/v1", document.RootElement.GetProperty("schema").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal("allowed", document.RootElement.GetProperty("payload").GetProperty("result").GetString());
    }

    [Fact]
    public void OmitsOversizedPayloadWhileRetainingTheSequencedEventIdentity()
    {
        var bytes = EventStreamJsonWriter.Serialize(Event("{\"text\":\"" + new string('x', EventStreamJsonWriter.MaximumMessageBytes * 2) + "\"}"));
        using var document = JsonDocument.Parse(bytes);

        Assert.True(bytes.Length <= EventStreamJsonWriter.MaximumMessageBytes);
        Assert.Equal("event-1", document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("sequence").GetInt32());
        Assert.True(document.RootElement.GetProperty("payloadOmitted").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("payload", out _));
    }

    private static SessionEvent Event(string payload) => new(
        "event-1",
        "session-1",
        7,
        SessionEventKind.ValidationCompleted,
        3,
        null,
        null,
        payload,
        DateTimeOffset.Parse("2026-07-15T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}
