using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.Features.Duplicates.SelectEmbeddingChunks;

public interface IEmbeddingChunkSelector
{
    IReadOnlyList<EmbeddingChunk> CreateChunks(
        ImportantNodeEligibility eligibility,
        IReadOnlyList<EmbeddingChunkSource> sources);
}
