namespace Archy.Features.Duplicates.PartitionEmbeddingCandidates;

public interface IEmbeddingCandidatePartitioner
{
    DuplicateComparisonPlan Partition(IReadOnlyList<DuplicateLogicCandidate> candidates);
}
