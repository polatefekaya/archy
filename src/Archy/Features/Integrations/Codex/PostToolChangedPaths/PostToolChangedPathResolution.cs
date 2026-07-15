namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public sealed record PostToolChangedPathResolution(
    PostToolKind ToolKind,
    IReadOnlyList<ResolvedPostToolPath> Paths)
{
    public IReadOnlyList<string> CodePaths => Paths
        .Where(static path => path.Category == ChangedPathCategory.Code)
        .Select(static path => path.RepositoryRelativePath)
        .ToArray();
}
