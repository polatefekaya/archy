using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;

/// <summary>Uses only exact duplicate-finding decisions, making ignored-pair quieting narrow and reversible.</summary>
public sealed class DuplicateDecisionFeedbackPolicy : IDuplicateDecisionFeedbackPolicy
{
    public DuplicateDecisionFeedback Evaluate(string findingId, IReadOnlyList<ArchitectureDecision> decisions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        ArgumentNullException.ThrowIfNull(decisions);
        if (decisions.Any(static decision => decision is null)) throw new ArgumentException("Decision history cannot contain null entries.", nameof(decisions));

        var relevant = decisions
            .Where(decision => string.Equals(decision.DecisionType, DuplicateReviewDecisionFactory.DecisionType, StringComparison.Ordinal) &&
                              decision.Targets.Any(target => target.Kind == ArchitectureTargetKind.DuplicateFinding && string.Equals(target.StableId, findingId, StringComparison.Ordinal)))
            .OrderByDescending(static decision => decision.OccurredAtUtc)
            .ThenByDescending(static decision => decision.DecisionId, StringComparer.Ordinal)
            .ToArray();
        var latest = relevant.FirstOrDefault();
        var adjustment = relevant.Length == 0 ? 0d : Math.Round(relevant.Average(static decision => Adjustment(decision.Resolution)), 6, MidpointRounding.AwayFromZero);
        return new DuplicateDecisionFeedback(
            latest?.Resolution == DecisionResolution.Ignored,
            latest?.Resolution,
            adjustment,
            [.. relevant.Select(static decision => decision.DecisionId)]);
    }

    private static double Adjustment(DecisionResolution resolution) => resolution switch
    {
        DecisionResolution.Accepted => .05d,
        DecisionResolution.Ignored => -.10d,
        DecisionResolution.Modified => -.03d,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown duplicate review resolution."),
    };
}
