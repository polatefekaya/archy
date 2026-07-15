namespace Archy.Features.Storage.WorkspaceDatabase.Initialize;

public sealed class WorkspaceDatabaseMigrationCatalog : IWorkspaceDatabaseMigrationCatalog
{
    private readonly WorkspaceDatabaseMigration[] _migrations;

    public WorkspaceDatabaseMigrationCatalog()
        : this(WorkspaceDatabaseSchema.Migrations)
    {
    }

    public WorkspaceDatabaseMigrationCatalog(IEnumerable<WorkspaceDatabaseMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = migrations.OrderBy(migration => migration.Version).ToArray();
        if (_migrations.Length == 0)
        {
            throw new ArgumentException("A migration catalog must contain at least one migration.", nameof(migrations));
        }

        for (var index = 0; index < _migrations.Length; index++)
        {
            if (_migrations[index].Version != index + 1)
            {
                throw new ArgumentException(
                    "Migration catalog versions must be contiguous and start at one.",
                    nameof(migrations));
            }
        }
    }

    public int LatestVersion => _migrations[^1].Version;

    public IReadOnlyList<WorkspaceDatabaseMigration> Migrations => _migrations;
}
