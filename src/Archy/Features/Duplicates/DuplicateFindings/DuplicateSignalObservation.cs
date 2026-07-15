namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed record DuplicateSignalObservation(
    string SignalObservationId,
    DuplicateSignalKind Kind,
    double Score,
    string EvidenceJson,
    DateTimeOffset ObservedAtUtc);
