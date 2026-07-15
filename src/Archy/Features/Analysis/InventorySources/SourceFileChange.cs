namespace Archy.Features.Analysis.InventorySources;

public sealed record SourceFileChange(
    SourceFileChangeKind Kind,
    SourceFile File,
    SourceFile? PreviousFile = null);
