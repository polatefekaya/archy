using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Duplicates.DuplicateFindings;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

public sealed record DuplicateInspectionRequest(
    DuplicateFindingObservation Observation,
    GraphRevisionSnapshot Graph,
    IReadOnlyList<ArchitectureDecision> Decisions);
