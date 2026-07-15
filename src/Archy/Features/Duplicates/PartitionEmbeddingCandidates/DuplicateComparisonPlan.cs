namespace Archy.Features.Duplicates.PartitionEmbeddingCandidates;

public sealed record DuplicateComparisonPlan(
    IReadOnlyList<DuplicateComparisonBand> ComparableBands,
    IReadOnlyList<DuplicateCandidateExclusion> Excluded);
