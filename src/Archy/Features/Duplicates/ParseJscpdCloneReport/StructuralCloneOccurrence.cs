namespace Archy.Features.Duplicates.ParseJscpdCloneReport;

public sealed record StructuralCloneOccurrence(
    CloneSourceRange First,
    CloneSourceRange Second,
    int TokenCount,
    int LineCount,
    string Format);
