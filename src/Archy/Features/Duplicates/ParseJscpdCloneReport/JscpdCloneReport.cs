namespace Archy.Features.Duplicates.ParseJscpdCloneReport;

public sealed record JscpdCloneReport(
    string ToolVersion,
    IReadOnlyList<StructuralCloneOccurrence> Clones);
