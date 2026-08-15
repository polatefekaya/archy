using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Queries.FindSimilar;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Similarity.RetrieveHybridCandidates;
using Archy.Features.Architecture.LayerMembership;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class FindSimilarMcpTool(
    IMediator? mediator = null,
    IEmbeddingCacheRepository? cacheRepository = null,
    IEmbeddingCacheResolver? cacheResolver = null,
    IEmbeddingModelProviderResolver? providerResolver = null,
    IRepositoryAiConsentPolicy? consentPolicy = null,
    ArchyConfiguration? configurationOverride = null) : IMcpTool
{
    public string Name => "find_similar";
    public string Description => "Read-only code similarity search. Query by intent, sourceStableId, repository-relative sourceFilePath, or sourceCode. With repository consent and configured embeddings, it generates a bounded query vector locally through the configured provider and compares it only with cached repository vectors; structural evidence always remains explainable.";
    public string InputSchemaJson => """{"type":"object","properties":{"query":{"type":"string","minLength":2,"description":"Natural-language intent, for example: create an architecture session."},"sourceStableId":{"type":"string","minLength":1,"description":"Persisted graph symbol or declaration ID to compare."},"sourceFilePath":{"type":"string","minLength":1,"description":"Repository-relative C# source file. With explicit embedding consent it can produce a cached query vector."},"sourceCode":{"type":"string","minLength":2,"maxLength":100000,"description":"Code snippet. With explicit embedding consent it can produce a cached query vector; otherwise structural comparison remains local."},"embeddingModel":{"type":"string","minLength":1,"description":"Indexed embedding model ID, for example text-embedding-3-large."},"limit":{"type":"integer","minimum":1,"maximum":50}},"anyOf":[{"required":["query"]},{"required":["sourceStableId"]},{"required":["sourceFilePath"]},{"required":["sourceCode"]}] }""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var limit = invocation.Arguments.TryGetProperty("limit", out var rawLimit) && rawLimit.TryGetInt32(out var parsed) ? parsed : 10;
        var source = invocation.Arguments.TryGetProperty("sourceStableId", out var rawSource) ? rawSource.GetString() : null;
        var sourceFilePath = invocation.Arguments.TryGetProperty("sourceFilePath", out var rawPath) ? rawPath.GetString() : null;
        var sourceCode = invocation.Arguments.TryGetProperty("sourceCode", out var rawCode) ? rawCode.GetString() : null;
        var suppliedQuery = invocation.Arguments.TryGetProperty("query", out var rawQuery) ? rawQuery.GetString() : null;
        var embeddingModel = invocation.Arguments.TryGetProperty("embeddingModel", out var rawModel) ? rawModel.GetString() : null;
        if (limit is < 1 or > 50) return McpToolResult.Failure("limit must be from 1 through 50.");
        if (!string.IsNullOrWhiteSpace(sourceCode) && sourceCode.Length > 100_000) return McpToolResult.Failure("sourceCode must be at most 100000 characters.");
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!snapshot.IsSuccess) return McpToolResult.Failure(snapshot.Problem!.Message);
        if (snapshot.Value is null) return McpToolResult.Success("{\"content\":[{\"type\":\"text\",\"text\":\"No active graph revision exists; similarity search abstains.\"}],\"structuredContent\":{\"abstention\":true,\"candidates\":[],\"embeddingEvidence\":\"unavailable\"}}");
        var resolved = ResolveInput(snapshot.Value, invocation.Workspace.RepositoryRoot, suppliedQuery, source, sourceFilePath, sourceCode);
        if (!resolved.IsSuccess) return McpToolResult.Failure(resolved.Error!);
        source = resolved.SourceStableId;
        var effectiveConfiguration = configurationOverride;
        if (effectiveConfiguration is null && mediator is not null)
        {
            var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(invocation.Workspace.RepositoryRoot, null, null), cancellationToken);
            if (configuration.IsSuccess)
            {
                effectiveConfiguration = configuration.Value!.Configuration;
            }
        }
        embeddingModel ??= effectiveConfiguration?.Model.EmbeddingModel;
        IReadOnlyList<SimilarCodeCandidate> candidates;
        try { candidates = SimilarCodeFinder.Find(snapshot.Value, new SimilarCodeQuery(resolved.Query!, source, limit, ToSimilarityPolicy(effectiveConfiguration), ResolveLayerMembership(snapshot.Value, effectiveConfiguration))); }
        catch (ArgumentException exception) { return McpToolResult.Failure(exception.Message); }
        var embeddingStatus = "unavailable";
        var embeddingReason = "No configured compatible embedding evidence is available; structural evidence remains ranked and explainable.";
        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            var repository = cacheRepository ?? new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
            var cached = await repository.ListByModelAsync(invocation.Workspace.StateLocation, embeddingModel, 2048, cancellationToken);
            if (!cached.IsSuccess) return McpToolResult.Failure(cached.Problem!.Message);
            var semantic = await ResolveQueryVectorAsync(
                invocation,
                snapshot.Value.Revision,
                resolved,
                source,
                embeddingModel,
                effectiveConfiguration,
                repository,
                cancellationToken);
            var sourceVector = semantic.Vector is null && !string.IsNullOrWhiteSpace(source)
                ? cached.Value!.FirstOrDefault(entry => entry.Key.MethodStableId == source)?.Vector
                : semantic.Vector;
            if (sourceVector is not null)
            {
                embeddingStatus = semantic.Status ?? "cached";
                embeddingReason = semantic.Reason ?? $"Cached cosine similarity from model '{embeddingModel}'.";
                candidates = candidates.Select(candidate =>
                {
                    var target = cached.Value!.FirstOrDefault(entry => entry.Key.MethodStableId == candidate.StableId && entry.Vector.Count == sourceVector.Count);
                    if (target is null) return candidate;
                    var score = Cosine(sourceVector, target.Vector);
                    return candidate with { Score = Math.Round((candidate.Score * .75d) + (score * .25d), 6), Evidence = [.. candidate.Evidence, new("embedding", score, embeddingReason)] };
                }).OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.StableId, StringComparer.Ordinal).ToArray();
            }
            else if (semantic.Reason is not null) embeddingReason = semantic.Reason;
        }
        var values = string.Join(',', candidates.Select(candidate => $"{{\"stableId\":{McpJson.String(candidate.StableId)},\"displayName\":{McpJson.String(candidate.DisplayName)},\"filePath\":{(candidate.FilePath is null ? "null" : McpJson.String(candidate.FilePath))},\"score\":{JsonNumber(candidate.Score)},\"evidence\":[{string.Join(',', candidate.Evidence.Select(e => $"{{\"kind\":{McpJson.String(e.Kind)},\"score\":{JsonNumber(e.Score)},\"reason\":{McpJson.String(e.Reason)}}}"))}],\"rawEvidence\":[{string.Join(',', (candidate.HybridEvidence ?? []).Select(e => $"{{\"kind\":{McpJson.String(e.Kind.ToString())},\"rawScore\":{JsonNumber(e.RawScore)},\"normalizedContribution\":{JsonNumber(e.NormalizedContribution)},\"available\":{McpJson.Boolean(e.IsAvailable)},\"detail\":{McpJson.String(e.Detail)}}}"))}]}}"));
        var text = candidates.Count == 0 ? "No persisted candidate matched the supplied evidence." : $"Found {candidates.Count} explainable similar-code candidate(s).";
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(text)}}}],\"structuredContent\":{{\"abstention\":{McpJson.Boolean(candidates.Count == 0)},\"candidates\":[{values}],\"embeddingEvidence\":{McpJson.String(embeddingStatus)},\"embeddingReason\":{McpJson.String(embeddingReason)},\"mutatedWorkingTree\":false}}}}");
    }

    private async ValueTask<SemanticVector> ResolveQueryVectorAsync(
        McpToolInvocation invocation,
        long graphRevision,
        SimilarInput input,
        string? sourceStableId,
        string modelId,
        ArchyConfiguration? effectiveConfiguration,
        IEmbeddingCacheRepository repository,
        CancellationToken cancellationToken)
    {
        // A stable graph symbol already has an indexed source representation.  Ad-hoc code,
        // files, and natural-language intent instead receive a separately cached query vector.
        if (input.IsStableSymbolOnly) return new(null, null, null);
        if (effectiveConfiguration is null || providerResolver is null || consentPolicy is null || cacheResolver is null)
            return new(null, null, "The MCP server has no configured embedding provider; structural similarity remains available.");
        var consent = consentPolicy.Evaluate(effectiveConfiguration.Memory, AiSourceSharingOperation.Embedding);
        if (!consent.IsAllowed) return new(null, null, consent.Reason);
        var provider = providerResolver.Resolve(effectiveConfiguration.Model.Provider);
        if (!provider.IsSuccess) return new(null, null, provider.Problem!.Message);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Query!)));
        var id = $"query:{hash.ToLowerInvariant()}";
        var chunk = new EmbeddingChunk(id, "<mcp-query>", input.Query!, hash);
        var resolved = await cacheResolver.ResolveAsync(invocation.Workspace.StateLocation, graphRevision, modelId, [chunk], provider.Value!, cancellationToken);
        if (!resolved.IsSuccess) return new(null, null, resolved.Failure!.Message);
        return new(resolved.Value!.VectorsByMethodStableId[id], resolved.Value.GeneratedCount > 0 ? "generated" : "cached", resolved.Value.GeneratedCount > 0 ? $"Generated a consented query embedding with model '{modelId}'." : $"Reused a cached query embedding from model '{modelId}'.");
    }

    private static SimilarInput ResolveInput(
        GraphRevisionSnapshot snapshot,
        string repositoryRoot,
        string? query,
        string? sourceStableId,
        string? sourceFilePath,
        string? sourceCode)
    {
        if (!string.IsNullOrWhiteSpace(query)) return SimilarInput.Success(query.Trim(), sourceStableId, SimilarInputKind.Query);
        if (!string.IsNullOrWhiteSpace(sourceCode)) return SimilarInput.Success(sourceCode, sourceStableId, SimilarInputKind.Code);
        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            var relative = sourceFilePath.Replace('\\', '/').TrimStart('/');
            var root = Path.GetFullPath(repositoryRoot);
            var file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file)) return SimilarInput.Failure("sourceFilePath must reference an existing file inside the repository.");
            if (new FileInfo(file).Length > 1_000_000) return SimilarInput.Failure("sourceFilePath exceeds the 1 MB local comparison limit.");
            try
            {
                var content = File.ReadAllText(file);
                var node = snapshot.Nodes.Where(node => string.Equals(node.FilePath, relative, StringComparison.Ordinal)).OrderBy(static node => node.StableId, StringComparer.Ordinal).FirstOrDefault();
                return SimilarInput.Success(content, sourceStableId ?? node?.StableId, SimilarInputKind.File);
            }
            catch (IOException) { return SimilarInput.Failure("sourceFilePath could not be read."); }
        }
        if (!string.IsNullOrWhiteSpace(sourceStableId))
        {
            var node = snapshot.Nodes.SingleOrDefault(node => string.Equals(node.StableId, sourceStableId, StringComparison.Ordinal));
            return node is null ? SimilarInput.Failure("sourceStableId does not exist in the active graph.") : SimilarInput.Success($"{node.DisplayName} {node.CanonicalKey}", sourceStableId, SimilarInputKind.Symbol);
        }
        return SimilarInput.Failure("Provide query, sourceStableId, sourceFilePath, or sourceCode.");
    }

    private sealed record SimilarInput(string? Query, string? SourceStableId, string? Error, SimilarInputKind Kind)
    {
        public bool IsSuccess => Error is null;
        public bool IsStableSymbolOnly => Kind == SimilarInputKind.Symbol;
        public static SimilarInput Success(string query, string? sourceStableId, SimilarInputKind kind) => new(query, sourceStableId, null, kind);
        public static SimilarInput Failure(string error) => new(null, null, error, SimilarInputKind.None);
    }
    private sealed record SemanticVector(IReadOnlyList<float>? Vector, string? Status, string? Reason);
    private static HybridSimilarityPolicy ToSimilarityPolicy(ArchyConfiguration? configuration)
    {
        var similarity = configuration?.Similarity;
        return similarity is null
            ? HybridSimilarityPolicy.Default
            : new HybridSimilarityPolicy(similarity.PolicyVersion, new HybridSimilarityWeights(
                similarity.EmbeddingWeight, similarity.SymbolWeight, similarity.SignatureWeight,
                similarity.DependencyNeighborhoodWeight, similarity.FileContextWeight, similarity.ModuleContextWeight));
    }
    private static Dictionary<string, string>? ResolveLayerMembership(GraphRevisionSnapshot snapshot, ArchyConfiguration? configuration)
    {
        if (configuration?.Layers.Length is not > 0) return null;
        var resolved = new LayerMembershipResolver().Resolve(snapshot.Nodes, configuration.Layers);
        if (!resolved.IsSuccess) return null;
        var assigned = resolved.Value!.Where(static membership => membership.State == LayerMembershipState.Assigned && membership.LayerName is not null)
            .ToDictionary(static membership => membership.NodeStableId, static membership => membership.LayerName!, StringComparer.Ordinal);
        return assigned.Count == 0 ? null : assigned;
    }
    private enum SimilarInputKind { None, Query, Code, File, Symbol }
    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right) { var dot=0d; var a=0d; var b=0d; for(var i=0;i<left.Count;i++){dot+=(double)left[i]*right[i];a+=(double)left[i]*left[i];b+=(double)right[i]*right[i];} return a == 0 || b == 0 ? 0 : Math.Round(dot / Math.Sqrt(a*b),6); }
    private static string JsonNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
