namespace Archy.Features.Health.HealthSnapshots;

public sealed record HealthSnapshot(
    string HealthSnapshotId,
    long GraphRevision,
    string CalculationVersion,
    double Score,
    IReadOnlyList<HealthMetricComponent> Components,
    IReadOnlyList<string> ResolutionDecisionIds,
    DateTimeOffset CreatedAtUtc);
