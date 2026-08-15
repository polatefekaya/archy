namespace Archy.Features.Similarity.ExplainReuseDecision;

public enum ReuseRecommendation
{
    Reuse,
    Extend,
    DoNotReuse,
    InsufficientEvidence,
}

public sealed record ReuseFactor(string Id, string Detail, bool Deterministic);

public sealed record ReuseExplanation(
    string CandidateStableId,
    ReuseRecommendation Recommendation,
    IReadOnlyList<ReuseFactor> SupportingFactors,
    IReadOnlyList<ReuseFactor> DifferentiatingFactors,
    IReadOnlyList<string> SuggestedNextChecks);

public sealed record ExplainReuseDecisionRequest(string CandidateStableId, string? ProposedStableId, string ProposedIntent)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CandidateStableId)) throw new ArgumentException("A candidate stable ID is required.", nameof(CandidateStableId));
        if (string.IsNullOrWhiteSpace(ProposedStableId) && string.IsNullOrWhiteSpace(ProposedIntent)) throw new ArgumentException("A proposed stable ID or intent is required.", nameof(ProposedIntent));
    }
}

public interface IReuseDecisionExplainer
{
    ReuseExplanation Explain(Archy.Features.Graph.ReadGraphRevision.GraphRevisionSnapshot snapshot, ExplainReuseDecisionRequest request);
}
