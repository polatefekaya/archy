using Archy.Features.Memory.ModelProviders.Contracts;

namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>Resolved vectors split by cache reuse and outbound generation for one explicitly authorized request.</summary>
public sealed record EmbeddingCacheResolution(
    string ModelId,
    IReadOnlyDictionary<string, IReadOnlyList<float>> VectorsByMethodStableId,
    int CacheHitCount,
    int GeneratedCount,
    ModelUsage GeneratedUsage);
