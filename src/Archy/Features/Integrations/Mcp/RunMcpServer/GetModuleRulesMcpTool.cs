using Archy.Features.Architecture.VerifyArchitecture;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Returns current deterministic layer classification and violations for one known graph node; unknown context explicitly abstains.</summary>
public sealed class GetModuleRulesMcpTool(IMediator mediator) : IMcpTool
{
    public string Name => "get_module_rules";
    public string Description => "Return resolved layer and deterministic rule context for a graph node.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"stableId":{"type":"string","minLength":1,"description":"Stable identifier of the graph node to classify."}},"required":["stableId"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("stableId", out var stable) || string.IsNullOrWhiteSpace(stable.GetString())) return McpToolResult.Failure("stableId is required.");
        var verification = await mediator.Send(new VerifyArchitectureCommand(invocation.Workspace.RepositoryRoot, null, null), cancellationToken);
        if (!verification.IsSuccess) return McpToolResult.Failure(verification.Problem!.Message);
        var result = verification.Value!;
        var membership = result.Evaluation.Membership.SingleOrDefault(item => string.Equals(item.NodeStableId, stable.GetString(), StringComparison.Ordinal));
        if (membership is null) return McpToolResult.Success("{\"content\":[{\"type\":\"text\",\"text\":\"No deterministic module-rule context is available for this target.\"}],\"structuredContent\":{\"abstention\":true}}");
        var violations = result.Evaluation.Violations.Where(item => item.Edge.SourceStableId == membership.NodeStableId || item.Edge.TargetStableId == membership.NodeStableId).Select(item => item.EdgeId).OrderBy(static id => id, StringComparer.Ordinal);
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Layer: {membership.LayerName ?? "unassigned"}; state: {membership.State}.\"}}],\"structuredContent\":{{\"graphRevision\":{result.GraphRevision},\"layer\":{(membership.LayerName is null ? "null" : $"\"{membership.LayerName}\"")},\"state\":\"{membership.State}\",\"matchingLayers\":[{string.Join(',', membership.MatchingLayerNames.Select(name => $"\"{name}\""))}],\"violationEdgeIds\":[{string.Join(',', violations.Select(id => $"\"{id}\""))}]}}}}");
    }
}
