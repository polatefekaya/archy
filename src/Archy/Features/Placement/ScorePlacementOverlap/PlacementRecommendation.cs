namespace Archy.Features.Placement.ScorePlacementOverlap;
public sealed record PlacementRecommendation(bool IsAbstention, string Reason, IReadOnlyList<PlacementScore> Candidates);
