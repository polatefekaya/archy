namespace Archy.Features.Storage.WorkspaceDatabase.Initialize;

public interface IWorkspaceDatabaseMigrationCatalog
{
    int LatestVersion { get; }

    IReadOnlyList<WorkspaceDatabaseMigration> Migrations { get; }
}
