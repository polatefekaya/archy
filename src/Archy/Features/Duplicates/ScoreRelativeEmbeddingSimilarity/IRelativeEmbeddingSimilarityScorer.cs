using Archy.Features.Duplicates.PartitionEmbeddingCandidates;

namespace Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

public interface IRelativeEmbeddingSimilarityScorer
{
    IReadOnlyList<RelativeEmbeddingSimilarity> Score(DuplicateComparisonBand band, IReadOnlyList<EmbeddingSimilarityCandidate> candidates);
}
