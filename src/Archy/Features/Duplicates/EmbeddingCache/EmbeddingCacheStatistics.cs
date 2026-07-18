namespace Archy.Features.Duplicates.EmbeddingCache;

public sealed record EmbeddingCacheStatistics(int Count, IReadOnlyDictionary<int, int> Dimensions, DateTimeOffset? LatestCreatedAtUtc);
