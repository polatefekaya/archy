using Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

namespace Archy.Features.Duplicates.RetrieveEmbeddingCandidates;

/// <summary>Uses deterministic 64-bit sign fingerprints to select a bounded set of exact-vector comparisons per revision.</summary>
public sealed class BoundedEmbeddingCandidateRetriever(EmbeddingRetrievalOptions options) : IBoundedEmbeddingCandidateRetriever
{
    public EmbeddingRetrievalResult Retrieve(long graphRevision, IReadOnlyList<EmbeddingSimilarityCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (graphRevision < 1 || options.MaxCandidatesPerMethod is < 1 or > 256 || candidates.Count > 100_000 ||
            candidates.Any(static candidate => candidate is null || string.IsNullOrWhiteSpace(candidate.MethodStableId) || candidate.Vector is null || candidate.Vector.Count == 0 || candidate.Vector.Any(static value => !float.IsFinite(value))) ||
            candidates.Select(static candidate => candidate.MethodStableId).Distinct(StringComparer.Ordinal).Count() != candidates.Count ||
            candidates.Select(static candidate => candidate.Vector.Count).Distinct().Count() > 1)
        {
            throw new ArgumentException("Bounded retrieval requires unique, equal-dimensional finite embeddings and a supported revision.", nameof(candidates));
        }

        var ordered = candidates.OrderBy(static candidate => candidate.MethodStableId, StringComparer.Ordinal).ToArray();
        var fingerprints = ordered.Select(static candidate => Fingerprint(candidate.Vector)).ToArray();
        var pairs = new SortedDictionary<(string Left, string Right), int>();
        var fingerprintComparisons = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var nearest = new List<(int Index, int Distance)>();
            for (var other = 0; other < ordered.Length; other++)
            {
                if (index == other) continue;
                fingerprintComparisons++;
                nearest.Add((other, Hamming(fingerprints[index], fingerprints[other])));
            }

            foreach (var item in nearest.OrderBy(static value => value.Distance).ThenBy(value => ordered[value.Index].MethodStableId, StringComparer.Ordinal).Take(options.MaxCandidatesPerMethod))
            {
                var left = ordered[index].MethodStableId;
                var right = ordered[item.Index].MethodStableId;
                var key = string.CompareOrdinal(left, right) <= 0 ? (left, right) : (right, left);
                if (!pairs.TryGetValue(key, out var distance) || item.Distance < distance) pairs[key] = item.Distance;
            }
        }

        return new EmbeddingRetrievalResult(graphRevision, ordered.Length, fingerprintComparisons, [.. pairs.Select(static pair => new RetrievedEmbeddingCandidatePair(pair.Key.Left, pair.Key.Right, pair.Value))]);
    }

    private static ulong Fingerprint(IReadOnlyList<float> vector)
    {
        ulong bits = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            var dot = 0d;
            for (var index = 0; index < vector.Count; index++) dot += vector[index] * ProjectionSign(bit, index);
            if (dot >= 0d) bits |= 1UL << bit;
        }

        return bits;
    }

    private static int ProjectionSign(int bit, int index)
    {
        var state = unchecked((uint)(bit + 1) * 747796405u + (uint)(index + 1) * 2891336453u);
        state ^= state >> 16;
        state *= 2246822519u;
        return (state & 1) == 0 ? -1 : 1;
    }

    private static int Hamming(ulong left, ulong right) => System.Numerics.BitOperations.PopCount(left ^ right);
}
