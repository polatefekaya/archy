namespace Archy.Features.Placement.ClusterRevisions;

public sealed record ClusterFact(string ClusterKey, IReadOnlyList<ClusterMemberFact> Members);
