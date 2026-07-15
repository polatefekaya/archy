namespace Archy.Features.Placement.ComputeModuleCohesion;

public sealed record ModuleCohesionMetric(string CommunityKey, int MemberCount, double InternalEdgeWeight, double ExternalEdgeWeight, double Cohesion);
