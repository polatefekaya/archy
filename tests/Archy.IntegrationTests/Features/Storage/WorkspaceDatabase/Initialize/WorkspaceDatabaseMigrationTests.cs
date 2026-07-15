using Archy.Features.Storage.WorkspaceDatabase.Initialize;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Storage.WorkspaceDatabase.Initialize;

public sealed class WorkspaceDatabaseMigrationTests
{
    [Fact]
    public async Task InitializeUpgradesLegacyVersionThreeDatabaseWithAnAuditedBackup()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var target = await CreateMigrationTargetAsync(fixture);
        await LegacyVersionThreeDatabase.SeedAsync(target.Location.DatabasePath);

        var result = await new WorkspaceDatabaseInitializer(TimeProvider.System).InitializeAsync(
            target.Location,
            target.Manifest,
            "configuration-hash",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(17, result.Value.SchemaVersion);
        await SqliteAssertions.AssertBootstrapAsync(target.Location.DatabasePath, target.Location.WorkspaceId);

        var backups = Directory.GetFiles(target.Location.DatabaseBackupDirectory, "*.db");
        var backup = Assert.Single(backups);
        var backupColumns = await SqliteTestDatabase.GetTableColumnsAsync(backup, "schema_migrations");
        Assert.DoesNotContain("name", backupColumns);
        Assert.DoesNotContain("checksum", backupColumns);
    }

    [Fact]
    public async Task InitializeRejectsATamperedMigrationChecksum()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        await SqliteTestDatabase.ExecuteAsync(
            initialized.Value.StateLocation.DatabasePath,
            "UPDATE schema_migrations SET checksum = 'tampered' WHERE version = 1;");

        var rejected = await fixture.InitializeAsync();

        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);
        Assert.Contains("checksum", rejected.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeRejectsADatabaseFromANewerBinaryWithoutDowngradingIt()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        await SqliteTestDatabase.ExecuteAsync(
            initialized.Value.StateLocation.DatabasePath,
            "INSERT INTO schema_migrations(version, name, checksum, applied_at_utc) VALUES (999, 'future', 'future', '2026-01-01T00:00:00.0000000+00:00');");

        var rejected = await fixture.InitializeAsync();

        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);
        Assert.Contains("Automatic downgrade is not supported", rejected.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(
            999L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT MAX(version) FROM schema_migrations;"));
        Assert.False(Directory.Exists(initialized.Value.StateLocation.DatabaseBackupDirectory));
    }

    [Fact]
    public async Task InitializeRollsBackAFailedMigrationAndLeavesTheDatabaseRecoverable()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var defaultCatalog = new WorkspaceDatabaseMigrationCatalog();
        var failingMigration = WorkspaceDatabaseMigration.Create(
            18,
            "simulate_interrupted_migration",
            """
            CREATE TABLE migration_rollback_probe (id INTEGER PRIMARY KEY);
            INSERT INTO migration_missing_table(id) VALUES (1);
            """);
        var failingCatalog = new WorkspaceDatabaseMigrationCatalog(
            [.. defaultCatalog.Migrations, failingMigration]);
        var failingInitializer = new WorkspaceDatabaseInitializer(TimeProvider.System, failingCatalog);

        var failed = await failingInitializer.InitializeAsync(
            initialized.Value.StateLocation,
            initialized.Value.Manifest,
            "configuration-hash",
            CancellationToken.None);

        Assert.False(failed.IsSuccess);
        Assert.Equal("storage_error", failed.Problem!.Code);
        Assert.Equal(
            17L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT MAX(version) FROM schema_migrations;"));
        Assert.False(
            await SqliteTestDatabase.TableExistsAsync(
                initialized.Value.StateLocation.DatabasePath,
                "migration_rollback_probe"));
        Assert.Single(Directory.GetFiles(initialized.Value.StateLocation.DatabaseBackupDirectory, "*.db"));

        var recovered = await new WorkspaceDatabaseInitializer(TimeProvider.System).InitializeAsync(
            initialized.Value.StateLocation,
            initialized.Value.Manifest,
            "configuration-hash",
            CancellationToken.None);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(17, recovered.Value.SchemaVersion);
    }

    [Fact]
    public async Task InitializeMigratesLegacyGraphSnapshotsIntoVersionedFacts()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var target = await CreateMigrationTargetAsync(fixture);
        await LegacyVersionFourGraphDatabase.SeedAsync(target.Location.DatabasePath, target.Location.WorkspaceId);

        var result = await new WorkspaceDatabaseInitializer(TimeProvider.System).InitializeAsync(
            target.Location,
            target.Manifest,
            "configuration-hash",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(17, result.Value.SchemaVersion);
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            target.Location.DatabasePath,
            target.Location.WorkspaceId,
            expectedRevision: 1);

        var nodeVersions = await GraphVersionReader.ReadNodeVersionsAsync(
            target.Location.DatabasePath,
            target.Location.WorkspaceId,
            "file:program");
        Assert.Collection(
            nodeVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Null(version.ValidToRevision);
                Assert.Equal("legacy:1:file:program", version.ContentHash);
            });

        var edgeVersions = await GraphVersionReader.ReadEdgeVersionsAsync(
            target.Location.DatabasePath,
            target.Location.WorkspaceId,
            "contains:file:program:type:program");
        Assert.Collection(
            edgeVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Null(version.ValidToRevision);
            });

        var backup = Assert.Single(Directory.GetFiles(target.Location.DatabaseBackupDirectory, "*.db"));
        Assert.DoesNotContain(
            "content_hash",
            await SqliteTestDatabase.GetTableColumnsAsync(backup, "graph_nodes"));
        Assert.False(await SqliteTestDatabase.TableExistsAsync(backup, "graph_node_versions"));
    }

    private static async Task<MigrationTarget> CreateMigrationTargetAsync(WorkspaceStateFixture fixture)
    {
        var workspace = await fixture.Repository.LocateHandler.Handle(
            new LocateWorkspaceCommand(fixture.Repository.Root),
            CancellationToken.None);
        Assert.True(workspace.IsSuccess);

        var location = new WorkspaceStateLayout().Resolve(workspace.Value, fixture.StateRoot);
        Assert.True(location.IsSuccess);
        Directory.CreateDirectory(location.Value.StateDirectory);
        var manifest = new WorkspaceManifest(
            1,
            location.Value.WorkspaceId,
            workspace.Value.RepositoryRoot,
            workspace.Value.GitMetadataPath,
            workspace.Value.IsLinkedWorktree,
            DateTimeOffset.UtcNow);

        return new MigrationTarget(location.Value, manifest);
    }

    private sealed record MigrationTarget(WorkspaceStateLocation Location, WorkspaceManifest Manifest);
}
