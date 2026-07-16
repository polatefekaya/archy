using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class RecordDecisionMcpTool(IHookEventPublisher? publisher = null) : IMcpTool
{
    public string Name => "record_decision";
    public string Description => "Persist an accepted, ignored, or modified decision against one or more architecture targets.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"decisionType":{"type":"string","minLength":1,"description":"Caller-defined decision category."},"resolution":{"type":"string","enum":["accepted","ignored","modified"],"description":"Recorded decision outcome."},"actorKind":{"type":"string","minLength":1,"description":"Kind of actor recording the decision."},"actorId":{"type":"string","minLength":1,"description":"Stable identifier of the decision actor."},"targets":{"type":"array","minItems":1,"items":{"type":"object","properties":{"kind":{"type":"string","enum":["graph_node","graph_edge","graph_symbol","rule","duplicate_finding","placement_finding"]},"stableId":{"type":"string","minLength":1}},"required":["kind","stableId"]},"description":"Distinct architecture targets affected by the decision."},"note":{"type":"string","description":"Optional explanation for the decision."},"sessionId":{"type":"string","minLength":1,"description":"Optional active Archy session identifier."},"graphRevision":{"type":"integer","minimum":1,"description":"Optional graph revision used when making the decision."}},"required":["decisionType","resolution","actorKind","actorId","targets"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!McpToolArguments.TryGetRequiredString(invocation.Arguments, "decisionType", out var decisionType)
            || !McpToolArguments.TryGetRequiredString(invocation.Arguments, "actorKind", out var actorKind)
            || !McpToolArguments.TryGetRequiredString(invocation.Arguments, "actorId", out var actorId)
            || !McpToolArguments.TryGetResolution(invocation.Arguments, out var resolution)
            || !McpToolArguments.TryGetTargets(invocation.Arguments, out var targets))
        {
            return McpToolResult.Failure("decisionType, actorKind, actorId, a supported resolution, and distinct targets are required.");
        }

        var note = invocation.Arguments.TryGetProperty("note", out var rawNote) ? rawNote.GetString() : null;
        var sessionId = invocation.Arguments.TryGetProperty("sessionId", out var rawSession) ? rawSession.GetString() : null;
        long? graphRevision = invocation.Arguments.TryGetProperty("graphRevision", out var rawRevision) && rawRevision.TryGetInt64(out var revision)
            ? revision
            : null;
        var repository = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var recorded = await repository.RecordAsync(invocation.Workspace.StateLocation, new ArchitectureDecisionFact(
            decisionType, resolution, note, actorKind, actorId, sessionId, graphRevision, targets), cancellationToken);
        if (!recorded.IsSuccess)
        {
            return McpToolResult.Failure(recorded.Problem!.Message);
        }

        var decision = recorded.Value!;
        if (publisher is not null && decision.SessionId is not null)
        {
            // The decision repository commits its decision event atomically with the decision.
            // Publish the persisted event, not a reconstructed approximation, to keep live and
            // reconnecting clients on the exact same sequence.
            var sessions = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
            var events = await sessions.ListEventsAsync(invocation.Workspace.StateLocation, decision.SessionId, cancellationToken);
            var decisionEvent = events.IsSuccess
                ? events.Value!.SingleOrDefault(item => item.Kind == SessionEventKind.DecisionRecorded && item.DecisionId == decision.DecisionId)
                : null;
            if (decisionEvent is not null)
            {
                publisher.Publish(new SessionEventPublication(decisionEvent));
            }
        }

        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Decision recorded.\"}}],\"structuredContent\":{{\"decisionId\":{McpJson.String(decision.DecisionId)},\"resolution\":{McpJson.String(decision.Resolution.ToString())},\"targetCount\":{decision.Targets.Count}}}}}");
    }
}
