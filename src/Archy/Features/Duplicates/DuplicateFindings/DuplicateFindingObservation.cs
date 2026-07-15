using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed record DuplicateFindingObservation(
    string FindingId,
    string FindingObservationId,
    ArchitectureTarget LeftTarget,
    ArchitectureTarget RightTarget,
    long GraphRevision,
    string AggregationVersion,
    double Confidence,
    string RationaleJson,
    IReadOnlyList<DuplicateSignalObservation> Signals,
    DateTimeOffset ObservedAtUtc);
