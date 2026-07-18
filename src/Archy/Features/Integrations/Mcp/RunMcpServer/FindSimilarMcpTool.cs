using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Queries.FindSimilar;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using System.Globalization;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class FindSimilarMcpTool : IMcpTool
{
    public string Name => "find_similar";
    public string Description => "Read-only ranking of persisted code candidates. Supply sourceStableId and embeddingModel to add cached cosine-similarity evidence when compatible vectors exist; unavailable evidence never means no semantic match exists.";
    public string InputSchemaJson => """{"type":"object","properties":{"query":{"type":"string","minLength":2,"description":"What you are looking for, for example: create an architecture session."},"sourceStableId":{"type":"string","minLength":1,"description":"Existing method stable ID used as the cached embedding source."},"embeddingModel":{"type":"string","minLength":1,"description":"Indexed embedding model ID, for example text-embedding-3-large."},"limit":{"type":"integer","minimum":1,"maximum":50}},"required":["query"]}""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("query", out var query) || string.IsNullOrWhiteSpace(query.GetString())) return McpToolResult.Failure("query is required.");
        var limit = invocation.Arguments.TryGetProperty("limit", out var rawLimit) && rawLimit.TryGetInt32(out var parsed) ? parsed : 10;
        var source = invocation.Arguments.TryGetProperty("sourceStableId", out var rawSource) ? rawSource.GetString() : null;
        var embeddingModel = invocation.Arguments.TryGetProperty("embeddingModel", out var rawModel) ? rawModel.GetString() : null;
        if (limit is < 1 or > 50) return McpToolResult.Failure("limit must be from 1 through 50.");
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!snapshot.IsSuccess) return McpToolResult.Failure(snapshot.Problem!.Message);
        if (snapshot.Value is null) return McpToolResult.Success("{\"content\":[{\"type\":\"text\",\"text\":\"No active graph revision exists; similarity search abstains.\"}],\"structuredContent\":{\"abstention\":true,\"candidates\":[],\"embeddingEvidence\":\"unavailable\"}}");
        IReadOnlyList<SimilarCodeCandidate> candidates;
        try { candidates = SimilarCodeFinder.Find(snapshot.Value, new SimilarCodeQuery(query.GetString()!, source, limit)); }
        catch (ArgumentException exception) { return McpToolResult.Failure(exception.Message); }
        var embeddingStatus = "unavailable";
        if (!string.IsNullOrWhiteSpace(embeddingModel) && !string.IsNullOrWhiteSpace(source))
        {
            var cached = await new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).ListByModelAsync(invocation.Workspace.StateLocation, embeddingModel, 2048, cancellationToken);
            if (!cached.IsSuccess) return McpToolResult.Failure(cached.Problem!.Message);
            var sourceVector = cached.Value!.FirstOrDefault(entry => entry.Key.MethodStableId == source);
            if (sourceVector is not null)
            {
                embeddingStatus = "cached";
                candidates = candidates.Select(candidate =>
                {
                    var target = cached.Value.FirstOrDefault(entry => entry.Key.MethodStableId == candidate.StableId && entry.Vector.Count == sourceVector.Vector.Count);
                    if (target is null) return candidate;
                    var score = Cosine(sourceVector.Vector, target.Vector);
                    return candidate with { Score = Math.Round((candidate.Score * .75d) + (score * .25d), 6), Evidence = [.. candidate.Evidence, new("embedding", score, $"Cached cosine similarity from model '{embeddingModel}'.")] };
                }).OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.StableId, StringComparer.Ordinal).ToArray();
            }
        }
        var values = string.Join(',', candidates.Select(candidate => $"{{\"stableId\":{McpJson.String(candidate.StableId)},\"displayName\":{McpJson.String(candidate.DisplayName)},\"filePath\":{(candidate.FilePath is null ? "null" : McpJson.String(candidate.FilePath))},\"score\":{JsonNumber(candidate.Score)},\"evidence\":[{string.Join(',', candidate.Evidence.Select(e => $"{{\"kind\":{McpJson.String(e.Kind)},\"score\":{JsonNumber(e.Score)},\"reason\":{McpJson.String(e.Reason)}}}"))}]}}"));
        var text = candidates.Count == 0 ? "No persisted candidate matched the supplied evidence." : $"Found {candidates.Count} explainable similar-code candidate(s).";
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(text)}}}],\"structuredContent\":{{\"abstention\":{McpJson.Boolean(candidates.Count == 0)},\"candidates\":[{values}],\"embeddingEvidence\":{McpJson.String(embeddingStatus)},\"embeddingReason\":\"Cached semantic evidence is used only for a supplied source symbol and compatible model vectors; this read-only tool does not generate embeddings.\",\"mutatedWorkingTree\":false}}}}");
    }
    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right) { var dot=0d; var a=0d; var b=0d; for(var i=0;i<left.Count;i++){dot+=(double)left[i]*right[i];a+=(double)left[i]*left[i];b+=(double)right[i]*right[i];} return a == 0 || b == 0 ? 0 : Math.Round(dot / Math.Sqrt(a*b),6); }
    private static string JsonNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
