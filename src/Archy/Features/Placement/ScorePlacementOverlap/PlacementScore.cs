namespace Archy.Features.Placement.ScorePlacementOverlap;
public sealed record PlacementScore(string ClusterKey, double WeightedOverlap, double MatchedWeight, double TotalWeight, int MatchedDependencyCount);
