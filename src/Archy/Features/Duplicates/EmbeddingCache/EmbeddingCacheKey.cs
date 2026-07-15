namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>The complete immutable identity of reusable embedding output.</summary>
public sealed record EmbeddingCacheKey(string MethodStableId, string ModelId, string ContentHash);
