using Archy.Features.Decisions.ArchitectureDecisions;
namespace Archy.Features.Placement.ApplyPlacementDecisionFeedback;
public sealed record PlacementReviewDecision(string PlacementFindingId, DecisionResolution Resolution, string? Note, string ActorKind, string ActorId, string? SessionId, long? GraphRevision);
