using Archy.Features.Duplicates.ParseJscpdCloneReport;

namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

/// <summary>One exact source-level clone occurrence attributed to an ordered pair of executable graph nodes.</summary>
public sealed record StructuralCloneSymbolEvidence(
    CloneSourceRange LeftRange,
    CloneSourceRange RightRange,
    int TokenCount,
    int LineCount,
    string Format);
