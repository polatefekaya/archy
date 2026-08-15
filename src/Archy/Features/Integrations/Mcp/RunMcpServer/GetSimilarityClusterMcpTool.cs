using Archy.Features.Similarity.BuildSimilarityClusters;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class GetSimilarityClusterMcpTool : IMcpTool
{
    public string Name => "get_similarity_cluster";
    public string Description => "Read an immutable persisted similarity cluster for the active graph revision, or an explicitly requested historical revision. It never changes the worktree or graph.";
    public string InputSchemaJson => """{"type":"object","properties":{"clusterId":{"type":"string","minLength":1},"graphRevision":{"type":"integer","minimum":1}},"required":["clusterId"]}""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var id = invocation.Arguments.TryGetProperty("clusterId", out var raw) ? raw.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return McpToolResult.Failure("clusterId is required.");
        var requestedRevision = invocation.Arguments.TryGetProperty("graphRevision", out var rawRevision) && rawRevision.TryGetInt64(out var parsedRevision) ? parsedRevision : (long?)null;
        if (requestedRevision is < 1) return McpToolResult.Failure("graphRevision must be a positive integer when supplied.");
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var active = await new GraphRevisionSnapshotReader(locks).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!active.IsSuccess) return McpToolResult.Failure(active.Problem!.Message);
        var activeRevision = active.Value?.Revision;
        var repository = new SimilarityClusterRepository(TimeProvider.System, locks);
        var effectiveRevision = requestedRevision ?? activeRevision;
        var cluster = effectiveRevision is null
            ? Archy.SharedKernel.Primitives.ResultFactory.Success<PersistedSimilarityCluster?>(null)
            : await repository.ReadClusterAsync(invocation.Workspace.StateLocation, id, effectiveRevision, cancellationToken);
        if (!cluster.IsSuccess) return McpToolResult.Failure(cluster.Problem!.Message);
        var latest = await repository.ReadLatestAsync(invocation.Workspace.StateLocation, null, cancellationToken);
        if (!latest.IsSuccess) return McpToolResult.Failure(latest.Problem!.Message);
        var staleRevision = requestedRevision is null && activeRevision is not null && latest.Value is not null && latest.Value.GraphRevision != activeRevision;
        if (cluster.Value is null)
        {
            var message = staleRevision
                ? $"No current similarity cluster matched the requested ID; the latest persisted cluster evidence is from graph revision {latest.Value!.GraphRevision}, while active graph revision is {activeRevision}."
                : "No similarity cluster matched the requested ID for the requested graph revision.";
            return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(message)}}}],\"structuredContent\":{{\"abstention\":true,\"stale\":{McpJson.Boolean(staleRevision)},\"activeGraphRevision\":{(activeRevision is null ? "null" : activeRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))},\"cluster\":null}}}}");
        }
        var members = string.Join(',', cluster.Value.Members.Select(MemberJson));
        var text = McpJson.String($"Similarity cluster: {cluster.Value.Label}.");
        var isHistorical = requestedRevision is not null && requestedRevision != activeRevision;
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"abstention\":false,\"stale\":{McpJson.Boolean(isHistorical)},\"historical\":{McpJson.Boolean(isHistorical)},\"graphRevision\":{effectiveRevision!.Value},\"activeGraphRevision\":{(activeRevision is null ? "null" : activeRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))},\"cluster\":{{\"id\":{McpJson.String(cluster.Value.Id)},\"label\":{McpJson.String(cluster.Value.Label)},\"members\":[{members}]}}}}}}");
    }

    private static string MemberJson(PersistedSimilarityClusterMember member)
    {
        var score = member.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var kinds = string.Join(',', member.EvidenceKinds.Select(kind => McpJson.String(kind.ToString())));
        return $"{{\"stableId\":{McpJson.String(member.StableId)},\"score\":{score},\"evidenceKinds\":[{kinds}]}}";
    }
}
