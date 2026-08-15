using System.Security.Cryptography;
using System.Text;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.RetrieveHybridCandidates;

namespace Archy.Features.Similarity.BuildSimilarityClusters;

public sealed record SimilarityClusterMember(string StableId, double Score, IReadOnlyList<SimilarityEvidenceKind> EvidenceKinds);
public sealed record SimilarityCluster(string Label, IReadOnlyList<SimilarityClusterMember> Members);
public sealed record SimilarityClusterBuildResult(long GraphRevision, string Algorithm, string AlgorithmVersion, string InputHash, int ComparedCandidatePairs, int EvaluatedCandidatePairs, IReadOnlyList<SimilarityCluster> Clusters);

/// <summary>Builds deterministic connected components from bounded hybrid candidate retrieval.</summary>
public sealed class SimilarityClusterBuilder
{
    public const string Algorithm = "bounded_hybrid_connected_components";
    public const string AlgorithmVersion = "1";
    private const int MaximumSeeds = 500;
    private const double MinimumScore = .60d;

    public static SimilarityClusterBuildResult Build(GraphRevisionSnapshot snapshot, HybridSimilarityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        policy ??= HybridSimilarityPolicy.Default;
        policy.Validate();
        var nodes = snapshot.Nodes.Where(node => !IsGenerated(node.FilePath)).OrderBy(node => node.StableId, StringComparer.Ordinal).Take(MaximumSeeds).ToArray();
        var candidateStableIds = nodes.Select(static node => node.StableId).ToHashSet(StringComparer.Ordinal);
        var retriever = new HybridSimilarityRetriever(); var edges = new List<(string Left, string Right, double Score, IReadOnlyList<SimilarityEvidenceKind> Kinds)>(); var evaluated = 0;
        foreach (var node in nodes)
        {
            var retrieved = retriever.Retrieve(snapshot, new(node.DisplayName + " " + node.CanonicalKey, node.StableId, node.FilePath, 50, policy.Weights, policy.Version, candidateStableIds));
            evaluated += retrieved.EvaluatedCandidateCount;
            var candidates = retrieved.Candidates;
            foreach (var candidate in candidates.Where(candidate => string.CompareOrdinal(node.StableId, candidate.StableId) < 0 && candidate.Score >= MinimumScore && candidate.Evidence.Count(evidence => evidence.IsAvailable && evidence.RawScore > 0d) >= 2))
                edges.Add((node.StableId, candidate.StableId, candidate.Score, candidate.Evidence.Where(evidence => evidence.IsAvailable && evidence.RawScore > 0d).Select(evidence => evidence.Kind).Order().ToArray()));
        }
        var components = Components(edges, nodes.Select(node => node.StableId));
        var clusters = components.Where(component => component.Count > 1).Select(component => Cluster(component, edges, snapshot)).OrderBy(cluster => cluster.Label, StringComparer.Ordinal).ToArray();
        var hashInput = policy.Version + "|" + string.Join('|', edges.OrderBy(edge => edge.Left, StringComparer.Ordinal).ThenBy(edge => edge.Right, StringComparer.Ordinal).Select(edge => $"{edge.Left}:{edge.Right}:{edge.Score:R}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
        return new(snapshot.Revision, Algorithm, $"{AlgorithmVersion}:{policy.Version}", hash, edges.Count, evaluated, clusters);
    }

    private static SimilarityCluster Cluster(IReadOnlyList<string> component, IReadOnlyList<(string Left, string Right, double Score, IReadOnlyList<SimilarityEvidenceKind> Kinds)> edges, GraphRevisionSnapshot snapshot)
    {
        var members = component.OrderBy(id => id, StringComparer.Ordinal).Select(id =>
        {
            var related = edges.Where(edge => edge.Left == id || edge.Right == id).ToArray();
            return new SimilarityClusterMember(id, related.Length == 0 ? 0d : Math.Round(related.Average(edge => edge.Score), 6), related.SelectMany(edge => edge.Kinds).Distinct().Order().ToArray());
        }).ToArray();
        var labelNode = snapshot.Nodes.Where(node => component.Contains(node.StableId, StringComparer.Ordinal)).OrderBy(node => node.FilePath, StringComparer.Ordinal).ThenBy(node => node.DisplayName, StringComparer.Ordinal).First();
        return new($"{labelNode.DisplayName}:{members[0].StableId}", members);
    }

    private static IReadOnlyList<string>[] Components(IReadOnlyList<(string Left, string Right, double Score, IReadOnlyList<SimilarityEvidenceKind> Kinds)> edges, IEnumerable<string> nodeIds)
    {
        var parent = nodeIds.ToDictionary(id => id, id => id, StringComparer.Ordinal);
        string Find(string id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
        foreach (var edge in edges) { var left = Find(edge.Left); var right = Find(edge.Right); if (left != right) parent[right] = left; }
        return parent.Keys.GroupBy(Find, StringComparer.Ordinal).Select(group => (IReadOnlyList<string>)group.Order(StringComparer.Ordinal).ToArray()).ToArray();
    }

    private static bool IsGenerated(string? path) => path?.Contains(".g.", StringComparison.OrdinalIgnoreCase) == true || path?.Contains("/obj/", StringComparison.OrdinalIgnoreCase) == true;
}
