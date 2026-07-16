using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class GetDecisionsMcpTool : IMcpTool
{
    public string Name => "get_decisions";
    public string Description => "Return recorded architecture decisions associated with one target.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"target":{"type":"object","properties":{"kind":{"type":"string","enum":["graph_node","graph_edge","graph_symbol","rule","duplicate_finding","placement_finding"],"description":"Canonical Archy target kind."},"stableId":{"type":"string","minLength":1,"description":"Stable identifier of the target."}},"required":["kind","stableId"]}},"required":["target"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!McpToolArguments.TryGetTarget(invocation.Arguments, out var target))
        {
            return McpToolResult.Failure("target.kind and target.stableId are required.");
        }

        var repository = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var decisions = await repository.ListForTargetAsync(invocation.Workspace.StateLocation, target, cancellationToken);
        if (!decisions.IsSuccess)
        {
            return McpToolResult.Failure(decisions.Problem!.Message);
        }

        var value = decisions.Value!;
        var items = string.Join(',', value.Select(static decision =>
            $"{{\"decisionId\":{McpJson.String(decision.DecisionId)},\"decisionType\":{McpJson.String(decision.DecisionType)},\"resolution\":{McpJson.String(decision.Resolution.ToString())},\"actorKind\":{McpJson.String(decision.ActorKind)},\"actorId\":{McpJson.String(decision.ActorId)},\"sessionId\":{(decision.SessionId is null ? "null" : McpJson.String(decision.SessionId))}}}"));
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String($"Found {value.Count} decision(s).")}}}],\"structuredContent\":{{\"target\":{{\"kind\":{McpJson.String(target.Kind.ToString())},\"stableId\":{McpJson.String(target.StableId)}}},\"decisions\":[{items}]}}}}");
    }
}
