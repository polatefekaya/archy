using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Queries.FindSimilar;

/// <summary>Ranks persisted graph evidence only; it never scans an unsaved proposal or creates embeddings.</summary>
public sealed class SimilarCodeFinder
{
    public static IReadOnlyList<SimilarCodeCandidate> Find(GraphRevisionSnapshot snapshot, SimilarCodeQuery query)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Query) || query.Limit is < 1 or > 50) throw new ArgumentException("Similarity queries require text and a limit from 1 through 50.", nameof(query));

        var source = query.SourceStableId is null ? null : snapshot.Nodes.SingleOrDefault(node => node.StableId == query.SourceStableId);
        var sourceNeighbors = source is null ? [] : Neighbors(snapshot, source.StableId);
        var queryTokens = Tokens(query.Query);
        return snapshot.Nodes
            .Where(node => node.StableId != query.SourceStableId && !string.IsNullOrWhiteSpace(node.DisplayName))
            .Select(node => Candidate(node, queryTokens, sourceNeighbors, snapshot))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.StableId, StringComparer.Ordinal)
            .Take(query.Limit).ToArray();
    }

    private static SimilarCodeCandidate Candidate(Graph.CommitGraphRevision.GraphNodeFact node, HashSet<string> queryTokens, HashSet<string> sourceNeighbors, GraphRevisionSnapshot snapshot)
    {
        var evidence = new List<SimilarCodeEvidence>();
        var nameScore = Jaccard(queryTokens, Tokens(node.DisplayName + " " + node.CanonicalKey));
        if (nameScore > 0) evidence.Add(new("symbol", nameScore, "Name and canonical identity share normalized tokens with the query."));
        var symbol = snapshot.Symbols.FirstOrDefault(symbol => symbol.NodeStableId == node.StableId);
        var signatureScore = symbol is null ? 0 : Jaccard(queryTokens, Tokens(symbol.FullyQualifiedName + " " + symbol.NormalizedSignature));
        if (signatureScore > 0) evidence.Add(new("structure", signatureScore, "Persisted symbol signature shares normalized tokens with the query."));
        var neighbors = sourceNeighbors.Count == 0 ? [] : Neighbors(snapshot, node.StableId);
        var dependencyScore = sourceNeighbors.Count == 0 ? 0 : Jaccard(sourceNeighbors, neighbors);
        if (dependencyScore > 0) evidence.Add(new("dependency_neighborhood", dependencyScore, "Candidate shares persisted dependency neighbors with the source symbol."));
        var score = Math.Round((nameScore * .55d) + (signatureScore * .25d) + (dependencyScore * .20d), 6, MidpointRounding.AwayFromZero);
        return new(node.StableId, node.DisplayName, node.FilePath, score, evidence);
    }

    private static HashSet<string> Neighbors(GraphRevisionSnapshot snapshot, string id) => snapshot.Edges.Where(edge => edge.SourceStableId == id || edge.TargetStableId == id).Select(edge => edge.SourceStableId == id ? edge.TargetStableId : edge.SourceStableId).ToHashSet(StringComparer.Ordinal);
    private static HashSet<string> Tokens(string value) => value.Split(['.', ':', '/', '\\', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).SelectMany(SplitCamel).Select(token => token.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    private static IEnumerable<string> SplitCamel(string value) { var start = 0; for (var i=1;i<value.Length;i++) if(char.IsUpper(value[i]) && char.IsLower(value[i-1])) { yield return value[start..i]; start=i; } if(start<value.Length) yield return value[start..]; }
    private static double Jaccard(HashSet<string> left, HashSet<string> right) { var union=left.Union(right).Count(); return union == 0 ? 0 : Math.Round(left.Intersect(right).Count()/(double)union,6,MidpointRounding.AwayFromZero); }
}
