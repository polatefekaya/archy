namespace Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

/// <summary>One finite non-zero embedding associated with a method selected for a single comparison band.</summary>
public sealed record EmbeddingSimilarityCandidate(string MethodStableId, IReadOnlyList<float> Vector);
