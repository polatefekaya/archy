namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

/// <summary>Complete result of resolving sidecar clone ranges against one immutable graph revision.</summary>
public sealed record StructuralCloneSymbolMapping(
    IReadOnlyList<StructuralCloneSymbolPair> Pairs,
    IReadOnlyList<StructuralCloneMappingExclusion> Excluded);
