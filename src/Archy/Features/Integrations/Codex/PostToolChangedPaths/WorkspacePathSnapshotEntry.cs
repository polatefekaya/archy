namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public sealed record WorkspacePathSnapshotEntry(
    string RepositoryRelativePath,
    bool Exists,
    bool IsBinary,
    string? ContentHash);
