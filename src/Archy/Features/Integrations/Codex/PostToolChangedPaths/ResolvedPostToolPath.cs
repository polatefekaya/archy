namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public sealed record ResolvedPostToolPath(
    string RepositoryRelativePath,
    ChangedPathCategory Category,
    IReadOnlyList<ChangedPathEvidence> Evidence);
