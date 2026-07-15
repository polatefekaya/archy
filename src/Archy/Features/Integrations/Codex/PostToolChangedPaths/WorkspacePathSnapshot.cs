namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

/// <summary>A bounded before/after snapshot supplied around one tool invocation, never a repository-wide scan.</summary>
public sealed record WorkspacePathSnapshot(IReadOnlyList<WorkspacePathSnapshotEntry> Entries);
