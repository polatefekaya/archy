using Microsoft.Data.Sqlite;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class LegacyVersionThreeDatabase
{
    public static async Task SeedAsync(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);
            CREATE TABLE repositories (workspace_id TEXT PRIMARY KEY, repository_root TEXT NOT NULL, configuration_hash TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
            CREATE TABLE analysis_runs (run_id TEXT PRIMARY KEY, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), analyzer_version TEXT NOT NULL, configuration_hash TEXT NOT NULL, repository_commit TEXT NULL, status TEXT NOT NULL, started_at_utc TEXT NOT NULL, completed_at_utc TEXT NULL, graph_revision INTEGER NULL);
            CREATE INDEX ix_analysis_runs_workspace_started ON analysis_runs(workspace_id, started_at_utc DESC);
            CREATE TABLE analysis_run_events (event_id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL REFERENCES analysis_runs(run_id), event_type TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, graph_revision INTEGER NULL);
            CREATE INDEX ix_analysis_run_events_run ON analysis_run_events(run_id, event_id);
            CREATE TABLE graph_revisions (revision INTEGER PRIMARY KEY AUTOINCREMENT, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), run_id TEXT NOT NULL UNIQUE REFERENCES analysis_runs(run_id), committed_at_utc TEXT NOT NULL);
            CREATE TABLE graph_nodes (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), stable_id TEXT NOT NULL, node_kind TEXT NOT NULL, canonical_key TEXT NOT NULL, display_name TEXT NOT NULL, file_path TEXT NULL, start_line INTEGER NULL, end_line INTEGER NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, stable_id));
            CREATE TABLE graph_edges (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), edge_id TEXT NOT NULL, source_stable_id TEXT NOT NULL, target_stable_id TEXT NOT NULL, edge_kind TEXT NOT NULL, normalized_join_key TEXT NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, edge_id));
            CREATE INDEX ix_graph_nodes_canonical ON graph_nodes(revision, canonical_key);
            CREATE INDEX ix_graph_edges_source ON graph_edges(revision, source_stable_id);
            CREATE INDEX ix_graph_edges_target ON graph_edges(revision, target_stable_id);
            INSERT INTO schema_migrations(version, applied_at_utc) VALUES (1, '2026-01-01T00:00:00.0000000+00:00');
            INSERT INTO schema_migrations(version, applied_at_utc) VALUES (2, '2026-01-01T00:00:01.0000000+00:00');
            INSERT INTO schema_migrations(version, applied_at_utc) VALUES (3, '2026-01-01T00:00:02.0000000+00:00');
            """;
        _ = await command.ExecuteNonQueryAsync();
    }
}
