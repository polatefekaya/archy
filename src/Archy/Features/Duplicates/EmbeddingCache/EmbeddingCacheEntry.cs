namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>Persisted provider output tied to the graph revision that first produced this exact method content.</summary>
public sealed record EmbeddingCacheEntry(
    EmbeddingCacheKey Key,
    IReadOnlyList<float> Vector,
    long CreatedGraphRevision,
    DateTimeOffset CreatedAtUtc);
