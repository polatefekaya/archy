namespace Archy.Features.Architecture.LayerMembership;

/// <summary>Deterministic layer classification for one graph node.</summary>
public sealed record LayerMembershipResolution(
    string NodeStableId,
    LayerMembershipState State,
    string? LayerName,
    IReadOnlyList<string> MatchingLayerNames,
    string? Detail);

public enum LayerMembershipState
{
    Assigned,
    Unassigned,
    Ambiguous,
    NotApplicable,
}
