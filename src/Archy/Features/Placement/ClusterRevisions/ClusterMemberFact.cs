using Archy.SharedKernel.Primitives;

namespace Archy.Features.Placement.ClusterRevisions;

public sealed record ClusterMemberFact(ArchitectureTarget Target, double MembershipWeight);
