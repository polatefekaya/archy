namespace Archy.Features.Health.HealthSnapshots;

public sealed record HealthSnapshotFact(
    long GraphRevision,
    string CalculationVersion,
    double Score,
    IReadOnlyList<HealthMetricComponentFact> Components,
    IReadOnlyList<string> ResolutionDecisionIds);
