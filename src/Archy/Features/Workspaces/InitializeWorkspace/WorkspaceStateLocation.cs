namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed record WorkspaceStateLocation(
    string WorkspaceId,
    string StateDirectory,
    string ManifestPath,
    string LockPath,
    string DatabasePath)
{
    public string DatabaseBackupDirectory => Path.Combine(StateDirectory, "backups");

    public string DatabaseDiagnosticsDirectory => Path.Combine(StateDirectory, "diagnostics");
}
