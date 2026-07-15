using Archy.Features.Duplicates.ParseJscpdCloneReport;

namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

/// <summary>Auditable reason why a sidecar clone could not safely become symbol-level evidence.</summary>
public sealed record StructuralCloneMappingExclusion(
    StructuralCloneOccurrence Occurrence,
    string Reason);
