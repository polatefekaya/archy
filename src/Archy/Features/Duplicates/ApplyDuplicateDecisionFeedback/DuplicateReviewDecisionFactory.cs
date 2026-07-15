using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;

/// <summary>Creates narrowly-targeted durable decision facts; broad pattern suppression is deliberately unsupported.</summary>
public static class DuplicateReviewDecisionFactory
{
    public const string DecisionType = "duplicate_review";

    public static ArchitectureDecisionFact Create(DuplicateReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (string.IsNullOrWhiteSpace(decision.FindingId) || string.IsNullOrWhiteSpace(decision.ActorKind) || string.IsNullOrWhiteSpace(decision.ActorId) ||
            !Enum.IsDefined(decision.Resolution) || decision.GraphRevision is < 1 || (decision.Note is not null && string.IsNullOrWhiteSpace(decision.Note)))
        {
            throw new ArgumentException("Duplicate review decisions require one exact finding, actor, supported resolution, and valid optional provenance.", nameof(decision));
        }

        return new ArchitectureDecisionFact(
            DecisionType,
            decision.Resolution,
            decision.Note,
            decision.ActorKind,
            decision.ActorId,
            decision.SessionId,
            decision.GraphRevision,
            [new ArchitectureTarget(ArchitectureTargetKind.DuplicateFinding, decision.FindingId)]);
    }
}
