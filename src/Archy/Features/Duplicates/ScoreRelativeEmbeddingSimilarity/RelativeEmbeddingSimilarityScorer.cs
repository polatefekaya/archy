using Archy.Features.Duplicates.PartitionEmbeddingCandidates;

namespace Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

/// <summary>Calibrates cosine similarities against their own bounded comparison band; it never emits a global absolute-threshold verdict.</summary>
public sealed class RelativeEmbeddingSimilarityScorer : IRelativeEmbeddingSimilarityScorer
{
    public IReadOnlyList<RelativeEmbeddingSimilarity> Score(DuplicateComparisonBand band, IReadOnlyList<EmbeddingSimilarityCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(band.Language) || band.MethodStableIds is null || band.MethodStableIds.Count < 2 ||
            candidates.Count != band.MethodStableIds.Count || candidates.Any(static candidate => candidate is null || string.IsNullOrWhiteSpace(candidate.MethodStableId) || candidate.Vector is null || candidate.Vector.Count == 0 || candidate.Vector.Any(static value => !float.IsFinite(value))) ||
            candidates.Select(static candidate => candidate.MethodStableId).Distinct(StringComparer.Ordinal).Count() != candidates.Count ||
            candidates.Select(static candidate => candidate.MethodStableId).OrderBy(static id => id, StringComparer.Ordinal).SequenceEqual(band.MethodStableIds.OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal) is false)
        {
            throw new ArgumentException("Relative embedding scoring requires exactly the finite embeddings selected by a comparison band.", nameof(candidates));
        }

        var dimension = candidates[0].Vector.Count;
        if (dimension == 0 || candidates.Any(candidate => candidate.Vector.Count != dimension || Norm(candidate.Vector) == 0d))
        {
            throw new ArgumentException("Embedding comparison requires equal-dimensional non-zero vectors.", nameof(candidates));
        }

        var ordered = candidates.OrderBy(static candidate => candidate.MethodStableId, StringComparer.Ordinal).ToArray();
        var rawPairs = new List<(EmbeddingSimilarityCandidate Left, EmbeddingSimilarityCandidate Right, double Raw)>();
        for (var left = 0; left < ordered.Length - 1; left++)
        {
            for (var right = left + 1; right < ordered.Length; right++)
            {
                rawPairs.Add((ordered[left], ordered[right], Cosine(ordered[left].Vector, ordered[right].Vector)));
            }
        }

        var mean = rawPairs.Average(static pair => pair.Raw);
        var variance = rawPairs.Average(pair => Math.Pow(pair.Raw - mean, 2));
        var standardDeviation = Math.Sqrt(variance);
        return [.. rawPairs
            .Select(pair => new RelativeEmbeddingSimilarity(
                pair.Left.MethodStableId,
                pair.Right.MethodStableId,
                band.Language,
                band.Complexity,
                band.LineCount,
                Round(pair.Raw),
                Round(standardDeviation == 0d ? 0d : (pair.Raw - mean) / standardDeviation),
                ordered.Length,
                rawPairs.Count,
                Round(mean),
                Round(standardDeviation)))
            .OrderByDescending(static result => result.RawCosineSimilarity)
            .ThenBy(static result => result.LeftMethodStableId, StringComparer.Ordinal)
            .ThenBy(static result => result.RightMethodStableId, StringComparer.Ordinal)];
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var dot = 0d;
        for (var index = 0; index < left.Count; index++) dot += (double)left[index] * right[index];
        return dot / (Norm(left) * Norm(right));
    }

    private static double Norm(IReadOnlyList<float> vector) => Math.Sqrt(vector.Sum(static value => (double)value * value));
    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
