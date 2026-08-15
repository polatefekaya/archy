using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class ImpactAnalysisMcpTool : IMcpTool
{
    public string Name => "impact_analysis";
    public string Description => "Return bounded direct and transitive graph impact with public API risk evidence. Advisory only.";
    public string InputSchemaJson => """{"type":"object","properties":{"stableId":{"type":"string","minLength":1},"direction":{"enum":["dependents","dependencies","both"]},"depth":{"type":"integer","minimum":1,"maximum":8},"maxNodes":{"type":"integer","minimum":1,"maximum":500}},"required":["stableId"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var stableId = invocation.Arguments.TryGetProperty("stableId", out var raw) ? raw.GetString() : null; if (string.IsNullOrWhiteSpace(stableId)) return McpToolResult.Failure("stableId is required.");
        var direction = invocation.Arguments.TryGetProperty("direction", out var rawDirection) ? ParseDirection(rawDirection.GetString()) : ImpactDirection.Both; if (direction is null) return McpToolResult.Failure("direction must be dependents, dependencies, or both.");
        var depth = invocation.Arguments.TryGetProperty("depth", out var rawDepth) && rawDepth.TryGetInt32(out var parsedDepth) ? parsedDepth : 3; var max = invocation.Arguments.TryGetProperty("maxNodes", out var rawMax) && rawMax.TryGetInt32(out var parsedMax) ? parsedMax : 100;
        var analyzer = new ImpactAnalyzer(new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System)), new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)));
        var result = await analyzer.AnalyzeAsync(invocation.Workspace.StateLocation, new(stableId, direction.Value, depth, max), cancellationToken); if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var direct = string.Join(',', result.Value!.Direct.Select(PathJson)); var transitive = string.Join(',', result.Value.Transitive.Select(PathJson)); var risks = string.Join(',', result.Value.Risks.Select(risk => $"{{\"id\":{McpJson.String(risk.Id)},\"detail\":{McpJson.String(risk.Detail)}}}"));
        var text = McpJson.String($"Impact analysis at graph revision {result.Value.GraphRevision}.");
        var publicSurface = string.Join(',', result.Value.PublicSurfaceStableIds.Select(McpJson.String));
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{result.Value.GraphRevision},\"abstention\":{McpJson.Boolean(result.Value.Abstained)},\"truncated\":{McpJson.Boolean(result.Value.IsTruncated)},\"direct\":[{direct}],\"transitive\":[{transitive}],\"publicSurfaceStableIds\":[{publicSurface}],\"risks\":[{risks}]}}}}");
    }
    private static ImpactDirection? ParseDirection(string? value) => value?.ToLowerInvariant() switch { null or "both" => ImpactDirection.Both, "dependents" => ImpactDirection.Dependents, "dependencies" => ImpactDirection.Dependencies, _ => null };
    private static string PathJson(ImpactPath path) => $"{{\"stableId\":{McpJson.String(path.StableId)},\"depth\":{path.Depth},\"edgeKind\":{McpJson.String(path.EdgeKind)},\"direction\":{McpJson.String(path.Direction.ToString())}}}";
}
