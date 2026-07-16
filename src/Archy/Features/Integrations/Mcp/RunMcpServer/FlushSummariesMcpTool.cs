using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Integrations.Codex.RecordHookEvents;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Durably requests a later summary worker flush; it does not make an external model call on the MCP request path.</summary>
public sealed class FlushSummariesMcpTool(IHookEventPublisher? publisher = null) : IMcpTool
{
    public string Name => "flush_summaries";
    public string Description => "Request durable, deferred summary flushing for an active session.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"sessionId":{"type":"string","minLength":1,"description":"The active Archy session identifier."},"payload":{"type":"object","description":"Optional caller context to include in the summary-batch request."}},"required":["sessionId"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!McpToolArguments.TryGetRequiredString(invocation.Arguments, "sessionId", out var sessionId))
        {
            return McpToolResult.Failure("sessionId is required.");
        }

        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var requested = await repository.AppendEventAsync(invocation.Workspace.StateLocation, sessionId, new SessionEventFact(
            SessionEventKind.SummaryBatchRequested,
            GraphRevision: null,
            Target: null,
            PayloadJson: McpToolArguments.GetPayload(invocation.Arguments, "payload")), cancellationToken);
        if (!requested.IsSuccess)
        {
            return McpToolResult.Failure(requested.Problem!.Message);
        }
        publisher?.Publish(new SessionEventPublication(requested.Value!));

        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Summary flush requested; model work remains deferred.\"}}],\"structuredContent\":{{\"sessionId\":{McpJson.String(sessionId)},\"eventId\":{McpJson.String(requested.Value!.EventId)},\"deferred\":true}}}}");
    }
}
