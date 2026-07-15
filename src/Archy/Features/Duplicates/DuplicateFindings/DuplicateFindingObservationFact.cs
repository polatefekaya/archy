using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed record DuplicateFindingObservationFact(
    ArchitectureTarget LeftTarget,
    ArchitectureTarget RightTarget,
    long GraphRevision,
    string AggregationVersion,
    double Confidence,
    string RationaleJson,
    IReadOnlyList<DuplicateSignalFact> Signals);
