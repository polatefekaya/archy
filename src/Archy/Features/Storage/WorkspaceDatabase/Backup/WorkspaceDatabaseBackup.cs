using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Storage.WorkspaceDatabase.Backup;

internal static class WorkspaceDatabaseBackup
{
    public static async Task CreateBeforeMigrationAsync(
        SqliteConnection sourceConnection,
        WorkspaceStateLocation location,
        int currentSchemaVersion,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await CreateAsync(
            sourceConnection,
            location,
            $"archy-before-schema-{currentSchemaVersion}",
            timeProvider,
            cancellationToken);
    }

    public static async Task<string> CreateManualAsync(
        SqliteConnection sourceConnection,
        WorkspaceStateLocation location,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        await CreateAsync(sourceConnection, location, "archy-backup", timeProvider, cancellationToken);

    private static async Task<string> CreateAsync(
        SqliteConnection sourceConnection,
        WorkspaceStateLocation location,
        string filePrefix,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceConnection);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(location.DatabaseBackupDirectory);
        var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(location.DatabaseBackupDirectory, $"{filePrefix}-{timestamp}-{Guid.NewGuid():N}.db");

        await using var destinationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await destinationConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(destinationConnection);
        return backupPath;
    }
}
