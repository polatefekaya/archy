using Archy.SharedKernel.Primitives;

namespace Archy.Features.Placement.ClusterRevisions;

public sealed record ClusterMember(ArchitectureTarget Target, double MembershipWeight);
