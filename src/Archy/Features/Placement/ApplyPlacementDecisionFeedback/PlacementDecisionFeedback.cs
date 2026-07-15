using Archy.Features.Decisions.ArchitectureDecisions;
namespace Archy.Features.Placement.ApplyPlacementDecisionFeedback;
public sealed record PlacementDecisionFeedback(bool SuppressExactContext,DecisionResolution? LatestResolution,IReadOnlyList<string> DecisionIds);
