using Archy.Features.Health.HealthSnapshots;

namespace Archy.Features.Health.ReadHealthComponentPages;

public sealed record HealthComponentPage(string SnapshotId, long GraphRevision, string CalculationVersion, double Score, int Offset, int Limit, int TotalCount, IReadOnlyList<HealthMetricComponent> Items);
