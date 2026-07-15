namespace Archy.Features.Placement.ScorePlacementOverlap;
public interface IPlacementOverlapScorer { PlacementRecommendation Score(IReadOnlyList<PlacementDependency> dependencies, IReadOnlyList<PlacementClusterCandidate> clusters); }
