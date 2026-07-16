using Archy.Features.Placement.ClusterRevisions;

namespace Archy.Features.Placement.ReadClusterMemberPages;

public sealed record ClusterMemberPage(string ClusterRevisionId,string ClusterId,string ClusterKey,long GraphRevision,string Algorithm,string AlgorithmVersion,int Offset,int Limit,int TotalCount,IReadOnlyList<ClusterMember> Items);
