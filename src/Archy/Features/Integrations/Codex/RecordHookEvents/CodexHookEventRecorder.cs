using System.Text.Json;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

/// <summary>Best-effort durability for post-tool outcomes. Recording must never hold a Codex turn hostage.</summary>
public sealed class CodexHookEventRecorder(IArchitectureSessionRepository sessions, IHookEventPublisher publisher) : ICodexHookEventRecorder
{
    public async ValueTask RecordValidationAsync(
        WorkspaceStateLocation location,
        string? sessionId,
        HookValidationEvent validationEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var payload = BuildPayload(validationEvent);
        try
        {
            var appended = await sessions.AppendEventAsync(
                location,
                sessionId,
                new SessionEventFact(SessionEventKind.ValidationCompleted, validationEvent.GraphRevision, null, payload),
                cancellationToken);
            if (appended.IsSuccess)
            {
                publisher.Publish(new SessionEventPublication(appended.Value!, validationEvent));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The post-edit verdict is still returned; telemetry failure must not suppress remediation.
        }
    }

    private static string BuildPayload(HookValidationEvent validationEvent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "codex-hook-validation/v1");
            writer.WriteNumber("changedCodePathCount", validationEvent.ChangedCodePathCount);
            writer.WriteBoolean("turnStopped", validationEvent.TurnStopped);
            writer.WritePropertyName("introducedFindingKeys");
            writer.WriteStartArray();
            foreach (var findingKey in validationEvent.IntroducedFindingKeys.OrderBy(static key => key, StringComparer.Ordinal))
            {
                writer.WriteStringValue(findingKey);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
