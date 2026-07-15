using System.Security.Cryptography;
using System.Text;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>Reads exact cache keys before one bounded provider batch; policy enforcement remains outside this explicit outbound boundary.</summary>
public sealed class EmbeddingCacheResolver(IEmbeddingCacheRepository cache) : IEmbeddingCacheResolver
{
    public async ValueTask<ModelProviderResult<EmbeddingCacheResolution>> ResolveAsync(
        WorkspaceStateLocation location,
        long graphRevision,
        string modelId,
        IReadOnlyList<EmbeddingChunk> chunks,
        IModelProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(provider);
        if (graphRevision < 1 || string.IsNullOrWhiteSpace(modelId) || chunks.Count > 2_048 || !provider.Descriptor.SupportsEmbeddings ||
            chunks.Any(static chunk => chunk is null || string.IsNullOrWhiteSpace(chunk.MethodStableId) || string.IsNullOrWhiteSpace(chunk.Content) || string.IsNullOrWhiteSpace(chunk.ContentHash)) ||
            chunks.Select(static chunk => chunk.MethodStableId).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
        {
            return Fail("Embedding cache resolution requires a capable provider and unique bounded chunks.");
        }

        var vectors = new SortedDictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
        var missing = new List<EmbeddingChunk>();
        foreach (var chunk in chunks.OrderBy(static chunk => chunk.MethodStableId, StringComparer.Ordinal))
        {
            var cached = await cache.FindAsync(location, new EmbeddingCacheKey(chunk.MethodStableId, modelId, chunk.ContentHash), cancellationToken);
            if (!cached.IsSuccess) return Fail(cached.Problem!.Message);
            if (cached.Value is null) missing.Add(chunk);
            else vectors.Add(chunk.MethodStableId, cached.Value.Vector);
        }

        if (missing.Count == 0)
        {
            return ModelProviderResults.Success(new EmbeddingCacheResolution(modelId, vectors, chunks.Count, 0, new ModelUsage(0, 0, 0, 0)));
        }

        var generated = await provider.GenerateEmbeddingsAsync(
            new EmbeddingGenerationRequest(RequestId(modelId, missing), modelId, [.. missing.Select(static chunk => new EmbeddingInput(chunk.MethodStableId, chunk.Content))]),
            cancellationToken);
        if (!generated.IsSuccess) return ModelProviderResults.Fail<EmbeddingCacheResolution>(generated.Failure!);
        if (!string.Equals(generated.Value!.ModelId, modelId, StringComparison.Ordinal) || !HasExpectedVectors(generated.Value.Vectors, missing))
        {
            return Fail("The embedding provider response did not contain one finite vector for each requested method.");
        }

        foreach (var chunk in missing)
        {
            var vector = generated.Value.Vectors.Single(item => string.Equals(item.InputId, chunk.MethodStableId, StringComparison.Ordinal)).Values;
            var stored = await cache.StoreAsync(location, new EmbeddingCacheEntry(new EmbeddingCacheKey(chunk.MethodStableId, modelId, chunk.ContentHash), vector, graphRevision, DateTimeOffset.MinValue), cancellationToken);
            if (!stored.IsSuccess) return Fail(stored.Problem!.Message);
            vectors.Add(chunk.MethodStableId, stored.Value.Vector);
        }

        return ModelProviderResults.Success(new EmbeddingCacheResolution(modelId, vectors, chunks.Count - missing.Count, missing.Count, generated.Value.Usage));
    }

    private static bool HasExpectedVectors(IReadOnlyList<EmbeddingVector> vectors, List<EmbeddingChunk> chunks) =>
        vectors.Count == chunks.Count && vectors.Select(static vector => vector.InputId).Distinct(StringComparer.Ordinal).Count() == vectors.Count &&
        vectors.All(static vector => vector.Values is { Count: > 0 and <= 32_768 } && vector.Values.All(float.IsFinite)) &&
        vectors.Select(static vector => vector.InputId).OrderBy(static id => id, StringComparer.Ordinal)
            .SequenceEqual(chunks.Select(static chunk => chunk.MethodStableId).OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string RequestId(string modelId, IReadOnlyList<EmbeddingChunk> chunks)
    {
        var material = string.Join("\n", chunks.Select(static chunk => $"{chunk.MethodStableId}:{chunk.ContentHash}"));
        return $"embedding-cache-v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{modelId}\n{material}"))).ToLowerInvariant()}";
    }

    private static ModelProviderResult<EmbeddingCacheResolution> Fail(string message) =>
        ModelProviderResults.Fail<EmbeddingCacheResolution>(new ModelProviderFailure(ModelProviderFailureKind.InvalidResponse, message, false, null));
}
