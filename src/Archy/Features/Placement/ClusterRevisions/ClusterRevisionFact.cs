namespace Archy.Features.Placement.ClusterRevisions;

public sealed record ClusterRevisionFact(
    long GraphRevision,
    string Algorithm,
    string AlgorithmVersion,
    string InputHash,
    IReadOnlyList<ClusterFact> Clusters);
