namespace Archy.Features.Duplicates.ParseJscpdCloneReport;

public sealed record CloneSourceRange(
    string RepositoryRelativePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
