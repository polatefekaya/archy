using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.DetectReintroducedCapability;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class FindReintroducedMcpTool : IMcpTool
{
    public string Name => "find_reintroduced";
    public string Description => "Find removed historical capabilities likely recreated by an active declaration, using only persisted graph-version evidence.";
    public string InputSchemaJson => """{"type":"object","properties":{"stableId":{"type":"string","minLength":1}},"required":["stableId"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var stableId = invocation.Arguments.TryGetProperty("stableId", out var raw) ? raw.GetString() : null; if (string.IsNullOrWhiteSpace(stableId)) return McpToolResult.Failure("stableId is required.");
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken); if (!snapshot.IsSuccess) return McpToolResult.Failure(snapshot.Problem!.Message); if (snapshot.Value is null) return McpToolResult.Success("{\"content\":[{\"type\":\"text\",\"text\":\"No active graph revision exists; historical detection abstains.\"}],\"structuredContent\":{\"abstention\":true,\"matches\":[]}}");
        var result = await new ReintroducedCapabilityDetector(new WorkspaceLockManager(TimeProvider.System)).FindAsync(invocation.Workspace.StateLocation, snapshot.Value, stableId, cancellationToken); if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var matches = string.Join(',', result.Value!.Matches.Select(MatchJson));
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(result.Value.Matches.Count == 0 ? "No reintroduced capability evidence was found." : $"Found {result.Value.Matches.Count} reintroduced capability match(es).")}}}],\"structuredContent\":{{\"graphRevision\":{result.Value.GraphRevision},\"abstention\":{McpJson.Boolean(result.Value.Abstained)},\"abstentionReason\":{(result.Value.AbstentionReason is null ? "null" : McpJson.String(result.Value.AbstentionReason))},\"matches\":[{matches}]}}}}");
    }

    private static string MatchJson(ReintroducedCapabilityMatch match)
    {
        var path = match.PreviousPath is null ? "null" : McpJson.String(match.PreviousPath);
        var score = match.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var decisions = string.Join(',', match.DecisionIds.Select(McpJson.String)); var sessions = string.Join(',', match.SessionIds.Select(McpJson.String));
        return $"{{\"historicalStableId\":{McpJson.String(match.HistoricalStableId)},\"historicalRevision\":{match.HistoricalRevision},\"removalRevision\":{match.RemovalRevision},\"previousPath\":{path},\"score\":{score},\"decisionIds\":[{decisions}],\"sessionIds\":[{sessions}]}}";
    }
}
