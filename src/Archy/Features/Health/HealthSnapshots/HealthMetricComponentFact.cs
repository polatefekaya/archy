namespace Archy.Features.Health.HealthSnapshots;

public sealed record HealthMetricComponentFact(
    string ComponentKey,
    double RawValue,
    double Weight,
    double WeightedContribution,
    string DetailJson);
