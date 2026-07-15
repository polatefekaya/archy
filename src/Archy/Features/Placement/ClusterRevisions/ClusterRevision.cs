namespace Archy.Features.Placement.ClusterRevisions;

public sealed record ClusterRevision(
    string ClusterRevisionId,
    long GraphRevision,
    string Algorithm,
    string AlgorithmVersion,
    string InputHash,
    IReadOnlyList<Cluster> Clusters,
    DateTimeOffset CreatedAtUtc);
