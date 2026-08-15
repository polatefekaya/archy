using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Similarity.ExplainReuseDecision;

/// <summary>Produces advisory reuse guidance from persisted structural facts; it never infers behavior.</summary>
public sealed class ReuseDecisionExplainer : IReuseDecisionExplainer
{
    public ReuseExplanation Explain(GraphRevisionSnapshot snapshot, ExplainReuseDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var candidate = snapshot.Nodes.SingleOrDefault(node => node.StableId == request.CandidateStableId);
        if (candidate is null)
            return new(request.CandidateStableId, ReuseRecommendation.InsufficientEvidence, [], [new("candidate_missing", "The candidate does not exist in the active graph revision.", true)], []);

        var proposed = request.ProposedStableId is null ? null : snapshot.Nodes.SingleOrDefault(node => node.StableId == request.ProposedStableId);
        if (request.ProposedStableId is not null && proposed is null)
            return new(candidate.StableId, ReuseRecommendation.InsufficientEvidence, [], [new("proposal_missing", "The proposed source stable ID does not exist in the active graph revision.", true)], []);

        var supporting = new List<ReuseFactor>();
        var differentiating = new List<ReuseFactor>();
        var candidateSymbol = Symbol(snapshot, candidate.StableId);
        var proposedSymbol = proposed is null ? null : Symbol(snapshot, proposed.StableId);
        var namesOverlap = Overlap(Tokens(request.ProposedIntent + " " + proposed?.DisplayName), Tokens(candidate.DisplayName + " " + candidate.CanonicalKey));
        if (namesOverlap > 0d) supporting.Add(new("symbol_overlap", "The proposed intent and candidate name share normalized structural tokens.", true));

        var signaturesComparable = candidateSymbol is not null && proposedSymbol is not null;
        var signaturesMatch = signaturesComparable && string.Equals(candidateSymbol!.NormalizedSignature, proposedSymbol!.NormalizedSignature, StringComparison.Ordinal);
        if (signaturesMatch) supporting.Add(new("public_signature_match", "The persisted normalized signatures match.", true));
        else if (signaturesComparable) differentiating.Add(new("public_signature_mismatch", "The persisted normalized signatures differ.", true));

        if (proposed is not null && SameDirectory(proposed.FilePath, candidate.FilePath)) supporting.Add(new("file_ownership_overlap", "Both declarations are in the same persisted directory.", true));
        else if (proposed is not null && !string.Equals(proposed.FilePath, candidate.FilePath, StringComparison.Ordinal)) differentiating.Add(new("file_ownership_difference", "The declarations have different persisted file ownership.", true));

        var candidatePublic = string.Equals(candidateSymbol?.Visibility, "public", StringComparison.OrdinalIgnoreCase);
        var proposedPublic = string.Equals(proposedSymbol?.Visibility, "public", StringComparison.OrdinalIgnoreCase);
        var recommendation = candidatePublic && proposedPublic && signaturesComparable && !signaturesMatch
            ? ReuseRecommendation.DoNotReuse
            : signaturesMatch && supporting.Count >= 2
                ? ReuseRecommendation.Reuse
                : namesOverlap > 0d && signaturesComparable && !signaturesMatch
                    ? ReuseRecommendation.Extend
                    : ReuseRecommendation.InsufficientEvidence;
        string[] checks = recommendation switch
        {
            ReuseRecommendation.Reuse => ["get_dependents", "check_violation"],
            ReuseRecommendation.Extend => ["get_dependents", "get_module_rules", "check_violation"],
            ReuseRecommendation.DoNotReuse => ["get_dependents", "check_violation"],
            _ => ["find_similar", "get_dependents"],
        };
        return new(candidate.StableId, recommendation, supporting, differentiating, checks);
    }

    private static GraphSymbolFact? Symbol(GraphRevisionSnapshot snapshot, string stableId) => snapshot.Symbols.FirstOrDefault(symbol => symbol.NodeStableId == stableId);
    private static bool SameDirectory(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(Path.GetDirectoryName(left), Path.GetDirectoryName(right), StringComparison.Ordinal);
    private static double Overlap(HashSet<string> left, HashSet<string> right) { var union = left.Union(right).Count(); return union == 0 ? 0d : left.Intersect(right).Count() / (double)union; }
    private static HashSet<string> Tokens(string? value) => (value ?? string.Empty).Split(['.', ':', '/', '\\', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).SelectMany(SplitCamel).Select(token => token.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    private static IEnumerable<string> SplitCamel(string value) { var start = 0; for (var index = 1; index < value.Length; index++) if (char.IsUpper(value[index]) && char.IsLower(value[index - 1])) { yield return value[start..index]; start = index; } if (start < value.Length) yield return value[start..]; }
}
