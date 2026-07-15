namespace Archy.Features.Workspaces.ReadRepositoryCommit;

/// <summary>Best available Git state for an immutable analysis run; a clean worktree is never required.</summary>
public sealed record RepositoryProvenance(
    string? HeadCommit,
    RepositoryWorktreeState WorktreeState,
    IReadOnlyList<string> ChangedPaths)
{
    public bool IsDirty => WorktreeState == RepositoryWorktreeState.Dirty;
}
