namespace Archy.Features.Workspaces.LocateWorkspace;

public sealed record LocatedWorkspace(
    string RepositoryRoot,
    string GitMetadataPath,
    bool IsLinkedWorktree);
