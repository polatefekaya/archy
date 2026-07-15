namespace Archy.Features.Analysis.InventorySources;

public sealed record SourceInventory(
    bool IsComplete,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyList<SourceFileChange> Changes,
    IReadOnlyList<SourceFile> ParseCandidates,
    IReadOnlyList<SourcePathExclusion> Exclusions,
    IReadOnlyList<SourceInventoryDiagnostic> Diagnostics);
