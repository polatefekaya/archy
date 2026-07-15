namespace Archy.Features.Health.HealthSnapshots;

public sealed record HealthMetricComponent(
    string ComponentKey,
    double RawValue,
    double Weight,
    double WeightedContribution,
    string DetailJson);
