using Archy.Features.Decisions.ArchitectureDecisions;
namespace Archy.Features.Placement.ApplyPlacementDecisionFeedback;
public interface IPlacementDecisionFeedbackPolicy { PlacementDecisionFeedback Evaluate(string placementFindingId,IReadOnlyList<ArchitectureDecision> decisions); }
