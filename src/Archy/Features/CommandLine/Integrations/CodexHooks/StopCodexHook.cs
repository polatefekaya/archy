using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Mediator;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

/// <summary>Settles one Codex session without making model availability a condition of task completion.</summary>
internal sealed class StopCodexHook(IMediator mediator)
{
    public async Task<int> RunAsync(CodexHookInput input, CancellationToken cancellationToken)
    {
        var workspace = await new McpWorkspaceContextFactory(mediator).CreateAsync(input.WorkingDirectory, cancellationToken);
        if (workspace is null)
        {
            await CodexHookResponseWriter.StopAsync("Archy session finalization is unavailable; task completion is not blocked.");
            return 0;
        }

        var sessions = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var flushRequested = await sessions.AppendEventAsync(
            workspace.StateLocation,
            input.SessionId,
            new SessionEventFact(SessionEventKind.SummaryBatchRequested, null, null, FinalFlushPayload()),
            cancellationToken);
        var ended = await sessions.EndAsync(
            workspace.StateLocation,
            input.SessionId,
            EndPayload(flushRequested.IsSuccess),
            cancellationToken);

        if (!ended.IsSuccess)
        {
            await CodexHookResponseWriter.StopAsync("Archy could not finalize this session; task completion is not blocked and summary work may be deferred.");
            return 0;
        }

        await CodexHookResponseWriter.StopAsync(flushRequested.IsSuccess
            ? "Archy session finalized; one final summary flush is durably requested and remains deferred."
            : "Archy session finalized; final summary flushing could not be requested and remains deferred.");
        return 0;
    }

    private static string FinalFlushPayload() => "{\"schema\":\"codex-stop/v1\",\"settleReason\":\"codex-stop\",\"isSessionEnd\":true}";

    private static string EndPayload(bool flushRequested)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "codex-stop/v1");
            writer.WriteBoolean("finalSummaryFlushRequested", flushRequested);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
