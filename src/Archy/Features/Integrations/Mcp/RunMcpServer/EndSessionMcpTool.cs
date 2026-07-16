using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Integrations.Codex.RecordHookEvents;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class EndSessionMcpTool(IHookEventPublisher? publisher = null) : IMcpTool
{
    public string Name => "end_session";
    public string Description => "End an architecture session without blocking the caller on deferred summary work.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"sessionId":{"type":"string","minLength":1,"description":"The active Archy session identifier."},"payload":{"type":"object","description":"Optional caller context persisted with the lifecycle event."}},"required":["sessionId"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!McpToolArguments.TryGetRequiredString(invocation.Arguments, "sessionId", out var sessionId))
        {
            return McpToolResult.Failure("sessionId is required.");
        }

        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var ended = await repository.EndAsync(invocation.Workspace.StateLocation, sessionId, McpToolArguments.GetPayload(invocation.Arguments, "payload"), cancellationToken);
        if (!ended.IsSuccess)
        {
            return McpToolResult.Failure(ended.Problem!.Message);
        }
        publisher?.Publish(new SessionEventPublication(ended.Value!));

        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Session ended.\"}}],\"structuredContent\":{{\"sessionId\":{McpJson.String(sessionId)},\"eventId\":{McpJson.String(ended.Value!.EventId)},\"summaryWorkDeferred\":true}}}}");
    }
}
