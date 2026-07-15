namespace Archy.Features.Placement.ComputeModuleCohesion;

public sealed record DetectedCommunity(string CommunityKey, IReadOnlyList<string> MemberStableIds);
