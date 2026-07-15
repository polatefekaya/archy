namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

/// <summary>A deterministic, unordered executable-symbol pair with all attributed structural-clone evidence.</summary>
public sealed record StructuralCloneSymbolPair(
    string PairId,
    string LeftStableId,
    string RightStableId,
    IReadOnlyList<StructuralCloneSymbolEvidence> Evidence);
