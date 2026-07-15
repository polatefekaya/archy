using System.Text.Json;
using Archy.Features.Storage.WorkspaceDatabase.Backup;
using Archy.Features.Storage.WorkspaceDatabase.Check;
using Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;
using Archy.Features.Storage.WorkspaceDatabase.Restore;
using Archy.Features.Storage.WorkspaceDatabase.Vacuum;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Storage.WorkspaceDatabase;

public sealed class WorkspaceDatabaseOperationsTests
{
    [Fact]
    public async Task CheckBackupAndDiagnosticsExportPreserveAHealthyWorkspaceWithoutSourceLeakage()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var checker = CreateChecker();

        var check = await checker.CheckAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(check.IsSuccess);
        Assert.True(check.Value.IsHealthy);
        Assert.Equal(17, check.Value.SchemaVersion);
        Assert.Null(check.Value.ActiveGraphRevision);

        var backup = await CreateBackupCreator().CreateAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(backup.IsSuccess);
        Assert.StartsWith(initialized.Value.StateLocation.DatabaseBackupDirectory, backup.Value.BackupPath, StringComparison.Ordinal);
        Assert.True(File.Exists(backup.Value.BackupPath));

        var diagnostics = await CreateDiagnosticsExporter().ExportAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(diagnostics.IsSuccess);
        Assert.True(File.Exists(diagnostics.Value.ExportPath));
        using var payload = JsonDocument.Parse(await File.ReadAllTextAsync(diagnostics.Value.ExportPath));
        Assert.Equal(1, payload.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal(initialized.Value.StateLocation.WorkspaceId, payload.RootElement.GetProperty("WorkspaceId").GetString());
        Assert.False((await File.ReadAllTextAsync(diagnostics.Value.ExportPath)).Contains(fixture.Repository.Root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreReplacesTheActiveGraphWithTheValidatedManagedBackup()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var firstRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("node:a", "hash:a:v1")]);
        var backup = await CreateBackupCreator().CreateAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(backup.IsSuccess);
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            backup.Value.BackupPath,
            initialized.Value.StateLocation.WorkspaceId,
            firstRevision);

        var secondRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("node:a", "hash:a:v2")]);
        Assert.True(secondRevision > firstRevision);
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            secondRevision);

        var restored = await CreateRestorer().RestoreAsync(
            initialized.Value.StateLocation,
            backup.Value.BackupPath,
            CancellationToken.None);
        Assert.True(restored.IsSuccess);
        Assert.Equal(17, restored.Value.SchemaVersion);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(backup.Value.BackupPath))),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath))));
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            firstRevision);
        Assert.Equal(
            1L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));

        var check = await CreateChecker().CheckAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(check.IsSuccess);
        Assert.True(check.Value.IsHealthy);
    }

    [Fact]
    public async Task RestoreRejectsUnmanagedMismatchedAndCorruptBackupsWithoutChangingTheWorkspace()
    {
        using var fixture = WorkspaceStateFixture.Create();
        using var foreignFixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        var foreignInitialized = await foreignFixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        Assert.True(foreignInitialized.IsSuccess);
        var originalHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath)));

        var unmanagedPath = Path.Combine(fixture.StateRoot, "unmanaged-backup.db");
        File.Copy(initialized.Value.StateLocation.DatabasePath, unmanagedPath);
        var unmanaged = await CreateRestorer().RestoreAsync(
            initialized.Value.StateLocation,
            unmanagedPath,
            CancellationToken.None);
        Assert.False(unmanaged.IsSuccess);
        Assert.Equal("validation", unmanaged.Problem!.Code);

        var foreignBackup = await CreateBackupCreator().CreateAsync(
            foreignInitialized.Value.StateLocation,
            CancellationToken.None);
        Assert.True(foreignBackup.IsSuccess);
        Directory.CreateDirectory(initialized.Value.StateLocation.DatabaseBackupDirectory);
        var mismatchedPath = Path.Combine(initialized.Value.StateLocation.DatabaseBackupDirectory, "foreign-workspace.db");
        File.Copy(foreignBackup.Value.BackupPath, mismatchedPath);
        var mismatched = await CreateRestorer().RestoreAsync(
            initialized.Value.StateLocation,
            mismatchedPath,
            CancellationToken.None);
        Assert.False(mismatched.IsSuccess);
        Assert.Equal("conflict", mismatched.Problem!.Code);

        var corruptPath = Path.Combine(initialized.Value.StateLocation.DatabaseBackupDirectory, "corrupt-backup.db");
        await File.WriteAllTextAsync(corruptPath, "not a SQLite database");
        var corrupt = await CreateRestorer().RestoreAsync(
            initialized.Value.StateLocation,
            corruptPath,
            CancellationToken.None);
        Assert.False(corrupt.IsSuccess);
        Assert.Equal("conflict", corrupt.Problem!.Code);

        Assert.Equal(
            originalHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(initialized.Value.StateLocation.DatabasePath))));
    }

    [Fact]
    public async Task CheckReportsAnInvalidActiveGraphPointerAsAnUnhealthyWorkspace()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await SqliteTestDatabase.ExecuteAsync(
            initialized.Value.StateLocation.DatabasePath,
            $"PRAGMA foreign_keys = OFF; UPDATE workspace_graph_states SET active_graph_revision = 999 WHERE workspace_id = '{initialized.Value.StateLocation.WorkspaceId}';");

        var report = await CreateChecker().CheckAsync(initialized.Value.StateLocation, CancellationToken.None);

        Assert.True(report.IsSuccess);
        Assert.False(report.Value.IsHealthy);
        Assert.Equal(999L, report.Value.ActiveGraphRevision);
        Assert.NotEmpty(report.Value.ForeignKeyViolations);
    }

    [Fact]
    public async Task VacuumRequiresTheMaintenanceThresholdUnlessForced()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var skipped = await CreateVacuumService().VacuumAsync(
            initialized.Value.StateLocation,
            force: false,
            CancellationToken.None);
        Assert.True(skipped.IsSuccess);
        Assert.False(skipped.Value.WasVacuumed);
        Assert.Contains("threshold", skipped.Value.Reason, StringComparison.OrdinalIgnoreCase);

        var result = await CreateVacuumService().VacuumAsync(
            initialized.Value.StateLocation,
            force: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasVacuumed);
        Assert.Contains("force", result.Value.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceDatabaseChecker CreateChecker() => new(CreateLockManager());

    private static WorkspaceDatabaseBackupCreator CreateBackupCreator() =>
        new(TimeProvider.System, CreateLockManager());

    private static WorkspaceDatabaseRestorer CreateRestorer() =>
        new(TimeProvider.System, CreateLockManager());

    private static WorkspaceDatabaseVacuumService CreateVacuumService() => new(CreateLockManager());

    private static WorkspaceDatabaseDiagnosticsExporter CreateDiagnosticsExporter()
    {
        var lockManager = CreateLockManager();
        return new WorkspaceDatabaseDiagnosticsExporter(
            TimeProvider.System,
            new WorkspaceDatabaseChecker(lockManager));
    }

    private static WorkspaceLockManager CreateLockManager() => new(TimeProvider.System);
}
