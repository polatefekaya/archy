namespace Archy.Features.Duplicates.IndexEmbeddings;
public sealed record EmbeddingIndexStatus(string ModelId, long? GraphRevision, int CachedVectorCount, IReadOnlyDictionary<int, int> Dimensions, DateTimeOffset? LatestCacheTimestamp, string Provider, string Consent);
