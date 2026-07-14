namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed record WorkspaceManifest(
    int FormatVersion,
    string WorkspaceId,
    string RepositoryRoot,
    string GitMetadataPath,
    bool IsLinkedWorktree,
    DateTimeOffset CreatedAtUtc);
