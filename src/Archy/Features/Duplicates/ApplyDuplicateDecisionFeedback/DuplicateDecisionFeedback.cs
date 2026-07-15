using Archy.Features.Decisions.ArchitectureDecisions;

namespace Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;

/// <summary>Auditable per-finding feedback; consumers must expose its evidence rather than silently changing global thresholds.</summary>
public sealed record DuplicateDecisionFeedback(
    bool SuppressRepeatedFinding,
    DecisionResolution? LatestResolution,
    double ConfidenceAdjustment,
    IReadOnlyList<string> DecisionIds);
