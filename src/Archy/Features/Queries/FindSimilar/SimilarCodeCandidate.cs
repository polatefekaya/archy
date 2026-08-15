namespace Archy.Features.Queries.FindSimilar;

using Archy.Features.Similarity.RetrieveHybridCandidates;

public sealed record SimilarCodeEvidence(string Kind, double Score, string Reason);

public sealed record SimilarCodeCandidate(
    string StableId,
    string DisplayName,
    string? FilePath,
    double Score,
    IReadOnlyList<SimilarCodeEvidence> Evidence,
    IReadOnlyList<SimilarityEvidence>? HybridEvidence = null);
