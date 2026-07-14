namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed record WorkspaceStateLocation(
    string WorkspaceId,
    string StateDirectory,
    string ManifestPath,
    string LockPath,
    string DatabasePath);
