using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Similarity.RetrieveHybridCandidates;

/// <summary>Evidence families used by advisory similarity retrieval.</summary>
public enum SimilarityEvidenceKind
{
    Embedding,
    Symbol,
    Signature,
    DependencyNeighborhood,
    FileContext,
    ModuleContext,
    CloneSignal,
    HistoricalRevision,
}

public enum SimilarityConfidence
{
    Insufficient,
    Low,
    Medium,
    High,
}

public sealed record HybridSimilarityWeights(
    double Embedding,
    double Symbol,
    double Signature,
    double DependencyNeighborhood,
    double FileContext,
    double ModuleContext)
{
    public static HybridSimilarityWeights Default { get; } = new(.25d, .25d, .15d, .15d, .10d, .10d);

    public bool IsValid => new[] { Embedding, Symbol, Signature, DependencyNeighborhood, FileContext, ModuleContext }
        .All(value => value is >= 0d and <= 1d)
        && Math.Abs(Embedding + Symbol + Signature + DependencyNeighborhood + FileContext + ModuleContext - 1d) < .000001d;

    public double For(SimilarityEvidenceKind kind) => kind switch
    {
        SimilarityEvidenceKind.Embedding => Embedding,
        SimilarityEvidenceKind.Symbol => Symbol,
        SimilarityEvidenceKind.Signature => Signature,
        SimilarityEvidenceKind.DependencyNeighborhood => DependencyNeighborhood,
        SimilarityEvidenceKind.FileContext => FileContext,
        SimilarityEvidenceKind.ModuleContext => ModuleContext,
        _ => 0d,
    };
}

/// <summary>Versioned local scoring policy. Persisted/adapted results can identify the exact deterministic weighting contract used.</summary>
public sealed record HybridSimilarityPolicy(string Version, HybridSimilarityWeights Weights)
{
    public static HybridSimilarityPolicy Default { get; } = new("hybrid-structural/v1", HybridSimilarityWeights.Default);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 80 || !Weights.IsValid)
            throw new ArgumentException("Similarity policy requires a bounded version identifier and weights that sum to 1.0.");
    }
}

public sealed record SimilarityEvidence(
    SimilarityEvidenceKind Kind,
    double RawScore,
    double NormalizedContribution,
    bool IsAvailable,
    string Detail);

public sealed record HybridSimilarityCandidate(
    string StableId,
    string DisplayName,
    string? FilePath,
    double Score,
    SimilarityConfidence Confidence,
    IReadOnlyList<SimilarityEvidence> Evidence,
    IReadOnlyList<string> ClusterIds);

/// <summary>Transport-neutral request. Adapters are responsible for resolving files and snippets safely.</summary>
public sealed record HybridSimilarityRequest(
    string Intent,
    string? SourceStableId,
    string? IntendedPath,
    int Limit = 10,
    HybridSimilarityWeights? Weights = null,
    string? PolicyVersion = null,
    IReadOnlySet<string>? CandidateStableIds = null,
    IReadOnlyDictionary<string, string>? LayerNamesByStableId = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Intent) && string.IsNullOrWhiteSpace(SourceStableId))
            throw new ArgumentException("Similarity retrieval requires an intent or source stable ID.", nameof(Intent));
        if (Limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(Limit), "Similarity retrieval limit must be from 1 through 50.");
        if (Weights is { IsValid: false })
            throw new ArgumentException("Similarity weights must be bounded, non-negative, and sum to 1.0.", nameof(Weights));
        if (PolicyVersion is { Length: > 80 } || PolicyVersion is { Length: 0 })
            throw new ArgumentException("Similarity policy version must be a non-empty identifier of at most 80 characters.", nameof(PolicyVersion));
        if (CandidateStableIds is not null && (CandidateStableIds.Count == 0 || CandidateStableIds.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Candidate stable IDs must be a non-empty set of non-empty identifiers when supplied.", nameof(CandidateStableIds));
    }
}

public sealed record HybridSimilarityResult(long GraphRevision, IReadOnlyList<HybridSimilarityCandidate> Candidates, bool Abstained, string? AbstentionReason, string PolicyVersion = "hybrid-structural/v1", int EvaluatedCandidateCount = 0);

public interface IHybridSimilarityRetriever
{
    HybridSimilarityResult Retrieve(GraphRevisionSnapshot snapshot, HybridSimilarityRequest request);
}
