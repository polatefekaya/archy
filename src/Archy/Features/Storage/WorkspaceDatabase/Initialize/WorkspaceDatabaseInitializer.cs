using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

using Archy.Features.Storage.WorkspaceDatabase.Backup;

namespace Archy.Features.Storage.WorkspaceDatabase.Initialize;

public sealed class WorkspaceDatabaseInitializer : IWorkspaceDatabaseInitializer
{
    private const int MigrationAuditMetadataVersion = 4;
    private static readonly Lazy<bool> SqliteInitialized = new(InitializeSqlite);
    private readonly IWorkspaceDatabaseMigrationCatalog migrationCatalog;
    private readonly TimeProvider timeProvider;

    public WorkspaceDatabaseInitializer(TimeProvider timeProvider)
        : this(timeProvider, new WorkspaceDatabaseMigrationCatalog())
    {
    }

    public WorkspaceDatabaseInitializer(
        TimeProvider timeProvider,
        IWorkspaceDatabaseMigrationCatalog migrationCatalog)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.migrationCatalog = migrationCatalog ?? throw new ArgumentNullException(nameof(migrationCatalog));
    }

    public async ValueTask<Result<InitializedWorkspaceDatabase>> InitializeAsync(
        WorkspaceStateLocation location,
        WorkspaceManifest manifest,
        string configurationHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationHash);
        cancellationToken.ThrowIfCancellationRequested();

        var databaseExisted = File.Exists(location.DatabasePath);
        try
        {
            _ = SqliteInitialized.Value;
            Directory.CreateDirectory(location.StateDirectory);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = location.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(
                connection,
                transaction: null,
                "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;",
                cancellationToken);

            var migrationHistoryExists = await MigrationHistoryExistsAsync(connection, cancellationToken);
            var auditColumns = migrationHistoryExists
                ? await GetAuditColumnsAsync(connection, cancellationToken)
                : MigrationHistoryAuditColumns.None;
            var appliedMigrations = migrationHistoryExists
                ? await LoadAppliedMigrationsAsync(connection, auditColumns, cancellationToken)
                : [];
            var plan = CreateMigrationPlan(appliedMigrations, auditColumns);
            if (!plan.IsSuccess)
            {
                return ResultFactory.Failure<InitializedWorkspaceDatabase>(plan.Problem!);
            }

            if (databaseExisted && plan.Value.PendingMigrations.Count > 0)
            {
                await WorkspaceDatabaseBackup.CreateBeforeMigrationAsync(
                    connection,
                    location,
                    plan.Value.CurrentVersion,
                    timeProvider,
                    cancellationToken);
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (!migrationHistoryExists)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    WorkspaceDatabaseSchema.CreateLegacyMigrationHistorySql,
                    cancellationToken);
            }

            var historyHasAuditColumns = auditColumns == MigrationHistoryAuditColumns.Complete;
            foreach (var migration in plan.Value.PendingMigrations)
            {
                await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
                if (migration.Version == MigrationAuditMetadataVersion)
                {
                    historyHasAuditColumns = true;
                }

                await RecordMigrationAsync(
                    connection,
                    transaction,
                    migration,
                    historyHasAuditColumns,
                    cancellationToken);
            }

            if (historyHasAuditColumns)
            {
                await BackfillMigrationAuditMetadataAsync(connection, transaction, cancellationToken);
            }

            await ExecuteAsync(
                connection,
                transaction,
                WorkspaceDatabaseSchema.RepositoryUpsertSql,
                cancellationToken,
                ("$workspaceId", manifest.WorkspaceId),
                ("$repositoryRoot", manifest.RepositoryRoot),
                ("$configurationHash", configurationHash),
                ("$updatedAt", timeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            await ExecuteAsync(
                connection,
                transaction,
                WorkspaceDatabaseSchema.WorkspaceGraphStateEnsureSql,
                cancellationToken,
                ("$workspaceId", manifest.WorkspaceId));
            await transaction.CommitAsync(cancellationToken);

            return ResultFactory.Success(new InitializedWorkspaceDatabase(migrationCatalog.LatestVersion));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<InitializedWorkspaceDatabase>(
                Problem.Storage($"Archy SQLite initialization failed: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<InitializedWorkspaceDatabase>(
                Problem.Storage($"Archy local state could not be initialized: {exception.Message}"));
        }
    }

    private Result<MigrationPlan> CreateMigrationPlan(
        IReadOnlyList<AppliedMigration> appliedMigrations,
        MigrationHistoryAuditColumns auditColumns)
    {
        if (auditColumns == MigrationHistoryAuditColumns.Partial)
        {
            return ResultFactory.Failure<MigrationPlan>(
                Problem.Conflict("The migration history has only part of its audit metadata. Restore a backup before retrying."));
        }

        var currentVersion = 0;
        foreach (var appliedMigration in appliedMigrations)
        {
            if (appliedMigration.Version > migrationCatalog.LatestVersion)
            {
                return ResultFactory.Failure<MigrationPlan>(
                    Problem.Conflict(
                        $"Database schema version {appliedMigration.Version} is newer than this Archy binary supports. Automatic downgrade is not supported."));
            }

            if (appliedMigration.Version != currentVersion + 1)
            {
                return ResultFactory.Failure<MigrationPlan>(
                    Problem.Conflict("The migration history is not contiguous. Restore a backup before retrying."));
            }

            currentVersion = appliedMigration.Version;
        }

        if (auditColumns == MigrationHistoryAuditColumns.None && currentVersion >= MigrationAuditMetadataVersion)
        {
            return ResultFactory.Failure<MigrationPlan>(
                Problem.Conflict("The migration history is missing the audit metadata required by its recorded schema version."));
        }

        if (auditColumns == MigrationHistoryAuditColumns.Complete && currentVersion < MigrationAuditMetadataVersion)
        {
            return ResultFactory.Failure<MigrationPlan>(
                Problem.Conflict("The migration history contains audit metadata without recording the migration that introduced it."));
        }

        if (auditColumns == MigrationHistoryAuditColumns.Complete)
        {
            foreach (var appliedMigration in appliedMigrations)
            {
                var expectedMigration = migrationCatalog.Migrations[appliedMigration.Version - 1];
                if (!string.Equals(appliedMigration.Name, expectedMigration.Name, StringComparison.Ordinal) ||
                    !string.Equals(appliedMigration.Checksum, expectedMigration.Checksum, StringComparison.Ordinal))
                {
                    return ResultFactory.Failure<MigrationPlan>(
                        Problem.Conflict(
                            $"Migration {appliedMigration.Version} does not match Archy's recorded name and checksum. Restore a backup before retrying."));
                }
            }
        }

        var pendingMigrations = migrationCatalog.Migrations
            .Where(migration => migration.Version > currentVersion)
            .ToArray();
        return ResultFactory.Success(new MigrationPlan(currentVersion, pendingMigrations));
    }

    private async Task BackfillMigrationAuditMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var migration in migrationCatalog.Migrations)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE schema_migrations SET name = $name, checksum = $checksum WHERE version = $version AND (name IS NULL OR checksum IS NULL);",
                cancellationToken,
                ("$name", migration.Name),
                ("$checksum", migration.Checksum),
                ("$version", migration.Version));
        }
    }

    private async Task RecordMigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WorkspaceDatabaseMigration migration,
        bool historyHasAuditColumns,
        CancellationToken cancellationToken)
    {
        var appliedAt = timeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (historyHasAuditColumns)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO schema_migrations(version, name, checksum, applied_at_utc) VALUES ($version, $name, $checksum, $appliedAt);",
                cancellationToken,
                ("$version", migration.Version),
                ("$name", migration.Name),
                ("$checksum", migration.Checksum),
                ("$appliedAt", appliedAt));
            return;
        }

        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO schema_migrations(version, applied_at_utc) VALUES ($version, $appliedAt);",
            cancellationToken,
            ("$version", migration.Version),
            ("$appliedAt", appliedAt));
    }

    private static async Task<MigrationHistoryAuditColumns> GetAuditColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(schema_migrations);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasName = false;
        var hasChecksum = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(1);
            hasName |= string.Equals(columnName, "name", StringComparison.Ordinal);
            hasChecksum |= string.Equals(columnName, "checksum", StringComparison.Ordinal);
        }

        return hasName == hasChecksum
            ? hasName ? MigrationHistoryAuditColumns.Complete : MigrationHistoryAuditColumns.None
            : MigrationHistoryAuditColumns.Partial;
    }

    private static async Task<bool> MigrationHistoryExistsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations');";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<AppliedMigration[]> LoadAppliedMigrationsAsync(
        SqliteConnection connection,
        MigrationHistoryAuditColumns auditColumns,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = auditColumns == MigrationHistoryAuditColumns.Complete
            ? "SELECT version, name, checksum FROM schema_migrations ORDER BY version;"
            : "SELECT version, NULL, NULL FROM schema_migrations ORDER BY version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new AppliedMigration(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return [.. migrations];
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool InitializeSqlite()
    {
        SQLitePCL.Batteries_V2.Init();
        return true;
    }

    private sealed record AppliedMigration(int Version, string? Name, string? Checksum);

    private sealed record MigrationPlan(int CurrentVersion, IReadOnlyList<WorkspaceDatabaseMigration> PendingMigrations);

    private enum MigrationHistoryAuditColumns
    {
        None,
        Complete,
        Partial,
    }
}
