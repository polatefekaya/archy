namespace Archy.Features.Queries.FindSimilar;

public sealed record SimilarCodeEvidence(string Kind, double Score, string Reason);

public sealed record SimilarCodeCandidate(
    string StableId,
    string DisplayName,
    string? FilePath,
    double Score,
    IReadOnlyList<SimilarCodeEvidence> Evidence);
