namespace Archy.Features.Placement.ClusterRevisions;

public sealed record Cluster(
    string ClusterId,
    string ClusterKey,
    IReadOnlyList<ClusterMember> Members);
