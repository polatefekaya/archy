using Archy.Features.Decisions.ArchitectureDecisions;

namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

/// <summary>Complete explainable duplicate-review payload, intentionally excluding raw source and embedding-vector data.</summary>
public sealed record DuplicateInspection(
    string FindingId,
    long GraphRevision,
    double Confidence,
    string RationaleJson,
    DuplicateInspectionSide Left,
    DuplicateInspectionSide Right,
    IReadOnlyList<DuplicateInspectionSignal> Signals,
    IReadOnlyList<ArchitectureDecision> Decisions,
    string SuggestedAction);
