using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Integrations.Codex.RecordHookEvents;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class StartSessionMcpTool(IHookEventPublisher? publisher = null) : IMcpTool
{
    public string Name => "start_session";
    public string Description => "Start an attributed architecture session for the current MCP client.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"actorKind":{"type":"string","minLength":1,"description":"Kind of actor starting the session, for example agent or user."},"actorId":{"type":"string","minLength":1,"description":"Stable identifier of the actor starting the session."},"sessionId":{"type":"string","minLength":1,"description":"Optional caller-chosen session identifier. A UUID-like ID is generated when omitted."},"clientKind":{"type":"string","minLength":1,"default":"mcp","description":"MCP client kind, for example codex."},"externalSessionId":{"type":"string","minLength":1,"description":"Optional client-side session identifier."},"payload":{"type":"object","description":"Optional caller context persisted with the session-start event."}},"required":["actorKind","actorId"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!McpToolArguments.TryGetRequiredString(invocation.Arguments, "actorKind", out var actorKind)
            || !McpToolArguments.TryGetRequiredString(invocation.Arguments, "actorId", out var actorId))
        {
            return McpToolResult.Failure("actorKind and actorId are required.");
        }

        var sessionId = McpToolArguments.TryGetRequiredString(invocation.Arguments, "sessionId", out var suppliedSessionId)
            ? suppliedSessionId
            : Guid.NewGuid().ToString("N");
        var clientKind = McpToolArguments.TryGetRequiredString(invocation.Arguments, "clientKind", out var suppliedClientKind)
            ? suppliedClientKind
            : "mcp";
        var externalSessionId = invocation.Arguments.TryGetProperty("externalSessionId", out var external) ? external.GetString() : null;
        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var started = await repository.StartAsync(invocation.Workspace.StateLocation, new SessionStartFact(
            sessionId, clientKind, externalSessionId, actorKind, actorId, McpToolArguments.GetPayload(invocation.Arguments, "payload")), cancellationToken);
        if (!started.IsSuccess)
        {
            return McpToolResult.Failure(started.Problem!.Message);
        }

        // StartAsync commits the lifecycle event in the same transaction as the session.
        // Read that durable event back before publishing it so every live message has the
        // canonical ID, sequence number, and timestamp used by replay.
        if (publisher is not null)
        {
            var events = await repository.ListEventsAsync(invocation.Workspace.StateLocation, started.Value!.SessionId, cancellationToken);
            var startedEvent = events.IsSuccess
                ? events.Value!.SingleOrDefault(static item => item.Kind == SessionEventKind.SessionStarted)
                : null;
            if (startedEvent is not null)
            {
                publisher.Publish(new SessionEventPublication(startedEvent));
            }
        }

        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Session started.\"}}],\"structuredContent\":{{\"sessionId\":{McpJson.String(started.Value!.SessionId)},\"clientKind\":{McpJson.String(started.Value.ClientKind)},\"ended\":false}}}}");
    }
}
