using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.InitializeWorkspaceDatabase;

public sealed class WorkspaceDatabaseInitializer(TimeProvider timeProvider) : IWorkspaceDatabaseInitializer
{
    private const int LatestSchemaVersion = 3;
    private static readonly Lazy<bool> SqliteInitialized = new(InitializeSqlite);

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
            await ExecuteAsync(connection, null, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;", cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);", cancellationToken);
            var maximum = await ScalarLongAsync(connection, transaction, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", cancellationToken);
            if (maximum > LatestSchemaVersion)
            {
                return ResultFactory.Failure<InitializedWorkspaceDatabase>(Problem.Conflict($"Database schema version {maximum} is newer than this Archy binary supports."));
            }

            if (maximum < 1)
            {
                await ExecuteAsync(connection, transaction, MigrationOneSql, cancellationToken);
                await ExecuteAsync(connection, transaction, "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (1, $appliedAt);", cancellationToken, ("$appliedAt", timeProvider.GetUtcNow().ToString("O")));
            }
            if (maximum < 2)
            {
                await ExecuteAsync(connection, transaction, MigrationTwoSql, cancellationToken);
                await ExecuteAsync(connection, transaction, "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (2, $appliedAt);", cancellationToken, ("$appliedAt", timeProvider.GetUtcNow().ToString("O")));
            }
            if (maximum < 3)
            {
                await ExecuteAsync(connection, transaction, MigrationThreeSql, cancellationToken);
                await ExecuteAsync(connection, transaction, "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (3, $appliedAt);", cancellationToken, ("$appliedAt", timeProvider.GetUtcNow().ToString("O")));
            }

            await ExecuteAsync(connection, transaction, RepositoryUpsertSql, cancellationToken,
                ("$workspaceId", manifest.WorkspaceId), ("$repositoryRoot", manifest.RepositoryRoot),
                ("$configurationHash", configurationHash), ("$updatedAt", timeProvider.GetUtcNow().ToString("O")));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new InitializedWorkspaceDatabase(LatestSchemaVersion));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<InitializedWorkspaceDatabase>(Problem.Storage($"Archy SQLite initialization failed: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<InitializedWorkspaceDatabase>(Problem.Storage($"Archy local state could not be initialized: {exception.Message}"));
        }
    }

    private static bool InitializeSqlite()
    {
        SQLitePCL.Batteries_V2.Init();
        return true;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private const string MigrationOneSql = """
        CREATE TABLE repositories (workspace_id TEXT PRIMARY KEY, repository_root TEXT NOT NULL, configuration_hash TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
        CREATE TABLE analysis_runs (run_id TEXT PRIMARY KEY, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), analyzer_version TEXT NOT NULL, configuration_hash TEXT NOT NULL, repository_commit TEXT NULL, status TEXT NOT NULL, started_at_utc TEXT NOT NULL, completed_at_utc TEXT NULL, graph_revision INTEGER NULL);
        CREATE INDEX ix_analysis_runs_workspace_started ON analysis_runs(workspace_id, started_at_utc DESC);
        """;

    private const string RepositoryUpsertSql = """
        INSERT INTO repositories(workspace_id, repository_root, configuration_hash, updated_at_utc)
        VALUES ($workspaceId, $repositoryRoot, $configurationHash, $updatedAt)
        ON CONFLICT(workspace_id) DO UPDATE SET
            repository_root = excluded.repository_root,
            configuration_hash = excluded.configuration_hash,
            updated_at_utc = excluded.updated_at_utc;
        """;

    private const string MigrationTwoSql = """
        CREATE TABLE analysis_run_events (event_id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL REFERENCES analysis_runs(run_id), event_type TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, graph_revision INTEGER NULL);
        CREATE INDEX ix_analysis_run_events_run ON analysis_run_events(run_id, event_id);
        """;

    private const string MigrationThreeSql = """
        CREATE TABLE graph_revisions (revision INTEGER PRIMARY KEY AUTOINCREMENT, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), run_id TEXT NOT NULL UNIQUE REFERENCES analysis_runs(run_id), committed_at_utc TEXT NOT NULL);
        CREATE TABLE graph_nodes (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), stable_id TEXT NOT NULL, node_kind TEXT NOT NULL, canonical_key TEXT NOT NULL, display_name TEXT NOT NULL, file_path TEXT NULL, start_line INTEGER NULL, end_line INTEGER NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, stable_id));
        CREATE TABLE graph_edges (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), edge_id TEXT NOT NULL, source_stable_id TEXT NOT NULL, target_stable_id TEXT NOT NULL, edge_kind TEXT NOT NULL, normalized_join_key TEXT NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, edge_id));
        CREATE INDEX ix_graph_nodes_canonical ON graph_nodes(revision, canonical_key);
        CREATE INDEX ix_graph_edges_source ON graph_edges(revision, source_stable_id);
        CREATE INDEX ix_graph_edges_target ON graph_edges(revision, target_stable_id);
        """;
}
