namespace Archy.Features.Duplicates.ReadDuplicateObservationPages;

public sealed record DuplicateObservationPageItem(string ObservationId, long GraphRevision, string AggregationVersion, double Confidence, string RationaleJson, DateTimeOffset ObservedAtUtc);
