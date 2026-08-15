using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Similarity.RetrieveHybridCandidates;

/// <summary>Deterministic, bounded similarity retrieval over persisted graph facts only.</summary>
public sealed class HybridSimilarityRetriever : IHybridSimilarityRetriever
{
    public HybridSimilarityResult Retrieve(GraphRevisionSnapshot snapshot, HybridSimilarityRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var source = request.SourceStableId is null ? null : snapshot.Nodes.SingleOrDefault(node => node.StableId == request.SourceStableId);
        if (request.SourceStableId is not null && source is null)
            return new(snapshot.Revision, [], true, "The requested source stable ID does not exist in the active graph revision.", EvaluatedCandidateCount: 0);

        var queryTokens = Tokens(request.Intent + " " + source?.DisplayName + " " + source?.CanonicalKey);
        var policy = request.Weights is null && request.PolicyVersion is null ? HybridSimilarityPolicy.Default : new HybridSimilarityPolicy(request.PolicyVersion ?? HybridSimilarityPolicy.Default.Version, request.Weights ?? HybridSimilarityWeights.Default);
        policy.Validate();
        var weights = policy.Weights;
        var candidatePool = snapshot.Nodes
            .Where(node => node.StableId != source?.StableId && !string.IsNullOrWhiteSpace(node.DisplayName))
            .Where(node => request.CandidateStableIds is null || request.CandidateStableIds.Contains(node.StableId))
            .ToArray();
        var candidates = candidatePool
            .Select(node => Score(snapshot, source, node, queryTokens, request.IntendedPath, weights, request.LayerNamesByStableId))
            .Where(candidate => candidate.Score > 0d)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenByDescending(_ => snapshot.Revision)
            .ThenBy(candidate => candidate.StableId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
        return new(snapshot.Revision, candidates, candidates.Length == 0, candidates.Length == 0 ? "No persisted candidate has available matching evidence." : null, policy.Version, candidatePool.Length);
    }

    private static HybridSimilarityCandidate Score(GraphRevisionSnapshot snapshot, GraphNodeFact? source, GraphNodeFact candidate, HashSet<string> queryTokens, string? intendedPath, HybridSimilarityWeights weights, IReadOnlyDictionary<string, string>? layers)
    {
        var evidence = new List<SimilarityEvidence>();
        Add(evidence, SimilarityEvidenceKind.Symbol, Jaccard(queryTokens, Tokens(candidate.DisplayName + " " + candidate.CanonicalKey)), "Normalized display-name and canonical-key token overlap.");

        var sourceSymbol = source is null ? null : snapshot.Symbols.FirstOrDefault(symbol => symbol.NodeStableId == source.StableId);
        var candidateSymbol = snapshot.Symbols.FirstOrDefault(symbol => symbol.NodeStableId == candidate.StableId);
        if (candidateSymbol is null)
            evidence.Add(new(SimilarityEvidenceKind.Signature, 0d, 0d, false, "No persisted signature is available for this candidate."));
        else
        {
            var signatureInput = sourceSymbol is null
                ? queryTokens
                : Tokens(sourceSymbol.FullyQualifiedName + " " + sourceSymbol.NormalizedSignature + " " + sourceSymbol.ReturnMetadataJson + " " + sourceSymbol.ParameterMetadataJson);
            var signatureTarget = Tokens(candidateSymbol.FullyQualifiedName + " " + candidateSymbol.NormalizedSignature + " " + candidateSymbol.ReturnMetadataJson + " " + candidateSymbol.ParameterMetadataJson);
            Add(evidence, SimilarityEvidenceKind.Signature, Jaccard(signatureInput, signatureTarget), "Persisted signature, return, and parameter metadata overlap.");
        }

        if (source is null)
            evidence.Add(new(SimilarityEvidenceKind.DependencyNeighborhood, 0d, 0d, false, "Dependency neighborhood requires a persisted source symbol."));
        else
        {
            var outgoing = Jaccard(Outgoing(snapshot, source.StableId), Outgoing(snapshot, candidate.StableId));
            var incoming = Jaccard(Incoming(snapshot, source.StableId), Incoming(snapshot, candidate.StableId));
            Add(evidence, SimilarityEvidenceKind.DependencyNeighborhood, (outgoing + incoming) / 2d, $"Direct outgoing overlap {outgoing:R}; direct incoming overlap {incoming:R}.");
        }

        var contextPath = intendedPath ?? source?.FilePath;
        if (string.IsNullOrWhiteSpace(contextPath) || string.IsNullOrWhiteSpace(candidate.FilePath))
            evidence.Add(new(SimilarityEvidenceKind.FileContext, 0d, 0d, false, "File context is unavailable because one side has no persisted path."));
        else
            Add(evidence, SimilarityEvidenceKind.FileContext, PathScore(contextPath, candidate.FilePath), "Directory, path-segment, and extension overlap.");

        evidence.Add(new(SimilarityEvidenceKind.Embedding, 0d, 0d, false, "Embedding evidence is unavailable in deterministic retrieval."));
        if (source is null || layers is null || !layers.TryGetValue(source.StableId, out var sourceLayer) || !layers.TryGetValue(candidate.StableId, out var candidateLayer))
            evidence.Add(new(SimilarityEvidenceKind.ModuleContext, 0d, 0d, false, "No deterministic configured layer membership is available for both declarations."));
        else
            Add(evidence, SimilarityEvidenceKind.ModuleContext, string.Equals(sourceLayer, candidateLayer, StringComparison.Ordinal) ? 1d : 0d, $"Configured layer membership: source '{sourceLayer}', candidate '{candidateLayer}'.");

        var availableWeight = evidence.Where(item => item.IsAvailable).Sum(item => weights.For(item.Kind));
        var normalized = evidence.Select(item => item.IsAvailable && availableWeight > 0d
            ? item with { NormalizedContribution = Round(item.RawScore * weights.For(item.Kind) / availableWeight) }
            : item).ToArray();
        var score = Round(normalized.Sum(item => item.NormalizedContribution));
        return new(candidate.StableId, candidate.DisplayName, candidate.FilePath, score, Confidence(score, normalized), normalized, []);
    }

    private static void Add(List<SimilarityEvidence> evidence, SimilarityEvidenceKind kind, double score, string detail) => evidence.Add(new(kind, Round(score), 0d, true, detail));
    private static SimilarityConfidence Confidence(double score, IReadOnlyList<SimilarityEvidence> evidence)
    {
        var corroborators = evidence.Count(item => item.IsAvailable && item.RawScore > 0d);
        return score switch
        {
            >= .70d when corroborators >= 3 => SimilarityConfidence.High,
            >= .40d when corroborators >= 2 => SimilarityConfidence.Medium,
            > 0d => SimilarityConfidence.Low,
            _ => SimilarityConfidence.Insufficient,
        };
    }

    private static HashSet<string> Incoming(GraphRevisionSnapshot snapshot, string id) => snapshot.Edges.Where(edge => edge.TargetStableId == id).Select(edge => edge.SourceStableId).ToHashSet(StringComparer.Ordinal);
    private static HashSet<string> Outgoing(GraphRevisionSnapshot snapshot, string id) => snapshot.Edges.Where(edge => edge.SourceStableId == id).Select(edge => edge.TargetStableId).ToHashSet(StringComparer.Ordinal);
    private static double PathScore(string left, string right)
    {
        var leftParts = Tokens(left); var rightParts = Tokens(right);
        var overlap = Jaccard(leftParts, rightParts);
        var leftDirectory = Path.GetDirectoryName(left)?.Replace('\\', '/') ?? string.Empty;
        var rightDirectory = Path.GetDirectoryName(right)?.Replace('\\', '/') ?? string.Empty;
        var sameDirectory = string.Equals(leftDirectory, rightDirectory, StringComparison.Ordinal) ? 1d : 0d;
        var sameExtension = string.Equals(Path.GetExtension(left), Path.GetExtension(right), StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
        return (overlap * .5d) + (sameDirectory * .3d) + (sameExtension * .2d);
    }
    private static HashSet<string> Tokens(string? value) => (value ?? string.Empty).Split(['.', ':', '/', '\\', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).SelectMany(SplitCamel).Select(token => token.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    private static IEnumerable<string> SplitCamel(string value) { var start = 0; for (var i = 1; i < value.Length; i++) if (char.IsUpper(value[i]) && char.IsLower(value[i - 1])) { yield return value[start..i]; start = i; } if (start < value.Length) yield return value[start..]; }
    private static double Jaccard(HashSet<string> left, HashSet<string> right) { var union = left.Union(right).Count(); return union == 0 ? 0d : left.Intersect(right).Count() / (double)union; }
    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
