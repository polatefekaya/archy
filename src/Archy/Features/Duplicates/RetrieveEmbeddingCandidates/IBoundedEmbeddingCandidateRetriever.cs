using Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

namespace Archy.Features.Duplicates.RetrieveEmbeddingCandidates;

public interface IBoundedEmbeddingCandidateRetriever
{
    EmbeddingRetrievalResult Retrieve(long graphRevision, IReadOnlyList<EmbeddingSimilarityCandidate> candidates);
}
