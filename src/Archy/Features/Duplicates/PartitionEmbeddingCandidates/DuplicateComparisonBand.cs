namespace Archy.Features.Duplicates.PartitionEmbeddingCandidates;

/// <summary>Only candidates within one language/size/complexity cohort may be compared semantically.</summary>
public sealed record DuplicateComparisonBand(
    string Language,
    DuplicateComplexityBand Complexity,
    DuplicateLineCountBand LineCount,
    IReadOnlyList<string> MethodStableIds);
