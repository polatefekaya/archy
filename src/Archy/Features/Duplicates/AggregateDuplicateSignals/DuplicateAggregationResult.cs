using Archy.Features.Duplicates.DuplicateFindings;

namespace Archy.Features.Duplicates.AggregateDuplicateSignals;

/// <summary>An advisory result always explains its signals; only a qualified result includes a durable finding fact.</summary>
public sealed record DuplicateAggregationResult(
    bool IsLikelyDuplicate,
    IReadOnlyList<DuplicateSignalKind> QualifiedSignals,
    string RationaleJson,
    DuplicateFindingObservationFact? Finding);
