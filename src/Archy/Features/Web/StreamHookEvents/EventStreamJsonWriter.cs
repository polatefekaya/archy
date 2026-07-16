using System.Text.Json;
using Archy.Features.Sessions.ArchitectureSessions;

namespace Archy.Features.Web.StreamHookEvents;

/// <summary>Serializes one sequenced session event with a fixed delivery-size ceiling.</summary>
public static class EventStreamJsonWriter
{
    public const int MaximumMessageBytes = 64 * 1024;

    public static byte[] Serialize(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        var payload = Build(sessionEvent, includePayload: true);
        return payload.Length <= MaximumMessageBytes
            ? payload
            : Build(sessionEvent, includePayload: false);
    }

    private static byte[] Build(SessionEvent sessionEvent, bool includePayload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "archy.event-stream/v1");
            writer.WriteString("eventId", sessionEvent.EventId);
            writer.WriteString("sessionId", sessionEvent.SessionId);
            writer.WriteNumber("sequence", sessionEvent.SequenceNumber);
            writer.WriteString("kind", SessionEventKindName(sessionEvent.Kind));
            if (sessionEvent.GraphRevision is null) writer.WriteNull("graphRevision"); else writer.WriteNumber("graphRevision", sessionEvent.GraphRevision.Value);
            writer.WriteString("occurredAtUtc", sessionEvent.OccurredAtUtc);
            if (includePayload)
            {
                writer.WritePropertyName("payload");
                writer.WriteRawValue(sessionEvent.PayloadJson, skipInputValidation: false);
            }
            else
            {
                writer.WriteBoolean("payloadOmitted", true);
                writer.WriteNumber("payloadUtf8Bytes", System.Text.Encoding.UTF8.GetByteCount(sessionEvent.PayloadJson));
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string SessionEventKindName(SessionEventKind kind) => kind switch
    {
        SessionEventKind.SessionStarted => "session_started",
        SessionEventKind.FileTouched => "file_touched",
        SessionEventKind.ValidationCompleted => "validation_completed",
        SessionEventKind.DecisionRecorded => "decision_recorded",
        SessionEventKind.SummaryBatchRequested => "summary_batch_requested",
        SessionEventKind.SessionEnded => "session_ended",
        _ => "unknown",
    };
}
