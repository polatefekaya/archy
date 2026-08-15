using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.RetrieveHybridCandidates;

namespace Archy.Features.Queries.FindSimilar;

/// <summary>Ranks persisted graph evidence only; it never scans an unsaved proposal or creates embeddings.</summary>
public sealed class SimilarCodeFinder
{
    public static IReadOnlyList<SimilarCodeCandidate> Find(GraphRevisionSnapshot snapshot, SimilarCodeQuery query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Query) || query.Limit is < 1 or > 50) throw new ArgumentException("Similarity queries require text and a limit from 1 through 50.", nameof(query));
        var policy = query.Policy ?? HybridSimilarityPolicy.Default;
        policy.Validate();
        var result = new HybridSimilarityRetriever().Retrieve(snapshot, new HybridSimilarityRequest(query.Query, query.SourceStableId, null, query.Limit, policy.Weights, policy.Version, LayerNamesByStableId: query.LayerNamesByStableId));
        return result.Candidates.Select(candidate => new SimilarCodeCandidate(
            candidate.StableId,
            candidate.DisplayName,
            candidate.FilePath,
            candidate.Score,
            candidate.Evidence.Where(evidence => evidence.IsAvailable && evidence.RawScore > 0d)
                .Select(evidence => new SimilarCodeEvidence(ToLegacyKind(evidence.Kind), evidence.RawScore, evidence.Detail))
                .ToArray(),
            candidate.Evidence)).ToArray();
    }

    private static string ToLegacyKind(SimilarityEvidenceKind kind) => kind switch
    {
        SimilarityEvidenceKind.Signature => "structure",
        SimilarityEvidenceKind.DependencyNeighborhood => "dependency_neighborhood",
        SimilarityEvidenceKind.FileContext => "file_context",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
