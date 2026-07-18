using Archy.Features.Memory.ModelProviders.Contracts;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public sealed record EmbeddingIndexResult(string ModelId, int CacheHits, int Generated, int RequestedChunks, ModelUsage Usage);
