namespace Archy.Features.Analysis.WatchWorkspaceChanges;

/// <summary>One settled watcher burst. The analysis inventory remains the authoritative final change classifier.</summary>
public sealed record WorkspaceChangeSet(
    DateTimeOffset SettledAtUtc,
    IReadOnlyList<string> RepositoryRelativePaths,
    bool RequiresFullInventoryReconciliation);
