using Microsoft.Data.Sqlite;
using Archy.Features.Storage.WorkspaceDatabase.Initialize;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class LegacyVersionFourGraphDatabase
{
    public static async Task SeedAsync(string databasePath, string workspaceId)
    {
        await LegacyVersionThreeDatabase.SeedAsync(databasePath);
        SQLitePCL.Batteries_V2.Init();
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await ExecuteAsync(
            connection,
            """
            ALTER TABLE schema_migrations ADD COLUMN name TEXT NULL;
            ALTER TABLE schema_migrations ADD COLUMN checksum TEXT NULL;
            INSERT INTO schema_migrations(version, applied_at_utc) VALUES (4, '2026-01-01T00:00:03.0000000+00:00');
            INSERT INTO repositories(workspace_id, repository_root, configuration_hash, updated_at_utc) VALUES ($workspaceId, '/legacy/repository', 'legacy-config', '2026-01-01T00:00:00.0000000+00:00');
            INSERT INTO analysis_runs(run_id, workspace_id, analyzer_version, configuration_hash, repository_commit, status, started_at_utc, completed_at_utc, graph_revision) VALUES ('legacy-run', $workspaceId, 'legacy', 'legacy-config', NULL, 'succeeded', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:01.0000000+00:00', 1);
            INSERT INTO graph_revisions(revision, workspace_id, run_id, committed_at_utc) VALUES (1, $workspaceId, 'legacy-run', '2026-01-01T00:00:01.0000000+00:00');
            INSERT INTO graph_nodes(revision, stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json) VALUES (1, 'file:program', 'file', 'Program.cs', 'Program.cs', 'Program.cs', 1, 10, 'legacy', 1, '{}');
            INSERT INTO graph_nodes(revision, stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json) VALUES (1, 'type:program', 'type', 'Archy.Program', 'Program', 'Program.cs', 3, 10, 'legacy', 1, '{}');
            INSERT INTO graph_edges(revision, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json) VALUES (1, 'contains:file:program:type:program', 'file:program', 'type:program', 'contains', 'program.cs#type:archy.program', 'legacy', 1, '{}');
            """,
            ("$workspaceId", workspaceId));

        var catalog = new WorkspaceDatabaseMigrationCatalog();
        foreach (var migration in catalog.Migrations.Where(migration => migration.Version <= 4))
        {
            await ExecuteAsync(
                connection,
                "UPDATE schema_migrations SET name = $name, checksum = $checksum WHERE version = $version;",
                ("$name", migration.Name),
                ("$checksum", migration.Checksum),
                ("$version", migration.Version));
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        _ = await command.ExecuteNonQueryAsync();
    }
}
