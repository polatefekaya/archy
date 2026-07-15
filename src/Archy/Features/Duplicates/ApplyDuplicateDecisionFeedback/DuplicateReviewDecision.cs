using Archy.Features.Decisions.ArchitectureDecisions;

namespace Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;

/// <summary>A user resolution for one stable duplicate-finding identity, ready for the shared durable decision repository.</summary>
public sealed record DuplicateReviewDecision(
    string FindingId,
    DecisionResolution Resolution,
    string? Note,
    string ActorKind,
    string ActorId,
    string? SessionId,
    long? GraphRevision);
