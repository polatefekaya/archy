namespace Archy.Features.Duplicates.PartitionEmbeddingCandidates;

/// <summary>Prevents flat-threshold, cross-language comparisons and routes intentionally data-shaped code away from duplicate-logic evidence.</summary>
public sealed class EmbeddingCandidatePartitioner : IEmbeddingCandidatePartitioner
{
    public DuplicateComparisonPlan Partition(IReadOnlyList<DuplicateLogicCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Any(static candidate => candidate is null || string.IsNullOrWhiteSpace(candidate.MethodStableId) || string.IsNullOrWhiteSpace(candidate.Language) || candidate.LogicalLineCount < 1 || candidate.CyclomaticComplexity < 1) ||
            candidates.Select(static candidate => candidate.MethodStableId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
        {
            throw new ArgumentException("Duplicate comparison candidates require unique methods, a language, and positive method metrics.", nameof(candidates));
        }

        var excluded = candidates
            .Where(static candidate => candidate.IsDataShape)
            .Select(static candidate => new DuplicateCandidateExclusion(candidate.MethodStableId, "data-shape"))
            .OrderBy(static exclusion => exclusion.MethodStableId, StringComparer.Ordinal)
            .ToArray();
        var bands = candidates
            .Where(static candidate => !candidate.IsDataShape)
            .GroupBy(candidate => (Language: candidate.Language.Trim().ToLowerInvariant(), Complexity: Complexity(candidate.CyclomaticComplexity), Lines: LineCount(candidate.LogicalLineCount)))
            .Where(static group => group.Count() >= 2)
            .OrderBy(static group => group.Key.Language, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Complexity)
            .ThenBy(static group => group.Key.Lines)
            .Select(static group => new DuplicateComparisonBand(group.Key.Language, group.Key.Complexity, group.Key.Lines, [.. group.Select(static candidate => candidate.MethodStableId).OrderBy(static id => id, StringComparer.Ordinal)]))
            .ToArray();
        return new DuplicateComparisonPlan(bands, excluded);
    }

    private static DuplicateComplexityBand Complexity(int value) => value switch
    {
        1 => DuplicateComplexityBand.Trivial,
        <= 3 => DuplicateComplexityBand.Simple,
        <= 8 => DuplicateComplexityBand.Moderate,
        _ => DuplicateComplexityBand.Complex,
    };

    private static DuplicateLineCountBand LineCount(int value) => value switch
    {
        <= 10 => DuplicateLineCountBand.Compact,
        <= 30 => DuplicateLineCountBand.Standard,
        _ => DuplicateLineCountBand.Extended,
    };
}
