using Archy.Features.Duplicates.SelectEmbeddingChunks;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public sealed record EmbeddingIndexPlan(long GraphRevision, IReadOnlyList<EmbeddingChunk> Chunks);
