using System.Text.Json;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class GetDependentsMcpTool : IMcpTool
{
    public string Name => "get_dependents";
    public string Description => "Return bounded direct or transitive graph dependents.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"stableId":{"type":"string","minLength":1,"description":"Stable identifier of the graph node whose dependents should be read."},"depth":{"type":"integer","minimum":1,"maximum":6,"default":3,"description":"Maximum transitive traversal depth."},"revision":{"type":"integer","minimum":1,"description":"Optional committed graph revision; the active revision is used when omitted."}},"required":["stableId"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("stableId", out var stable) || string.IsNullOrWhiteSpace(stable.GetString())) return McpToolResult.Failure("stableId is required.");
        var depth = invocation.Arguments.TryGetProperty("depth", out var rawDepth) && rawDepth.TryGetInt32(out var parsedDepth) ? parsedDepth : 3;
        long? revision = invocation.Arguments.TryGetProperty("revision", out var rawRevision) && rawRevision.TryGetInt64(out var parsedRevision) ? parsedRevision : null;
        var reader = new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System));
        var traversal = await reader.TraverseAsync(invocation.Workspace.StateLocation, new GraphTraversalQuery(stable.GetString()!, GraphTraversalDirection.Dependents, revision, depth), cancellationToken);
        if (!traversal.IsSuccess) return McpToolResult.Failure(traversal.Problem!.Message);
        var result = traversal.Value!;
        var edges = string.Join(',', result.Edges.Select(edge => $"{{\"source\":\"{Escape(edge.SourceStableId)}\",\"target\":\"{Escape(edge.TargetStableId)}\",\"kind\":\"{Escape(edge.EdgeKind)}\",\"confidence\":{edge.Confidence:R}}}"));
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"revision {result.Revision}; edges {result.Edges.Count}\"}}],\"structuredContent\":{{\"revision\":{result.Revision},\"truncated\":{result.IsTruncated.ToString().ToLowerInvariant()},\"edges\":[{edges}]}}}}");
    }

    private static string Escape(string value) => JsonEncodedText.Encode(value).ToString();
}
