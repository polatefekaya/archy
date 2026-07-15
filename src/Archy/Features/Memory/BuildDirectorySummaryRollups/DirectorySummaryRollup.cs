namespace Archy.Features.Memory.BuildDirectorySummaryRollups;

public sealed record DirectorySummaryRollup(
    string DirectoryStableId,
    string RepositoryRelativePath,
    string StructuralSummaryInput,
    IReadOnlyList<string> ChildSummaryVersionIds);
