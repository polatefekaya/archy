using Microsoft.Data.Sqlite;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class SqliteAssertions
{
    public static async Task AssertBootstrapAsync(string databasePath, string workspaceId)
    {
        await using var connection = await OpenAsync(databasePath);

        Assert.Equal(
            17L,
            await ScalarLongAsync(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17);"));
        Assert.Equal(
            17L,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM schema_migrations WHERE name IS NOT NULL AND checksum IS NOT NULL AND length(checksum) = 64;"));
        Assert.Equal(
            1L,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM repositories WHERE workspace_id = $workspaceId;",
                ("$workspaceId", workspaceId)));
        await AssertActiveGraphRevisionAsync(databasePath, workspaceId, expectedRevision: null);
    }

    public static async Task AssertAnalysisRunAsync(
        string databasePath,
        string runId,
        string expectedStatus,
        long? expectedGraphRevision,
        IReadOnlyList<string> expectedEvents)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, graph_revision FROM analysis_runs WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The analysis run was not persisted.");
        Assert.Equal(expectedStatus, reader.GetString(0));

        long? actualRevision = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        Assert.Equal(expectedGraphRevision, actualRevision);

        await using var eventCommand = connection.CreateCommand();
        eventCommand.CommandText = "SELECT event_type FROM analysis_run_events WHERE run_id = $runId ORDER BY event_id;";
        eventCommand.Parameters.AddWithValue("$runId", runId);
        await using var eventReader = await eventCommand.ExecuteReaderAsync();
        var actualEvents = new List<string>();
        while (await eventReader.ReadAsync())
        {
            actualEvents.Add(eventReader.GetString(0));
        }

        Assert.Equal(expectedEvents, actualEvents);
    }

    public static async Task AssertGraphRevisionAsync(
        string databasePath,
        long revision,
        string runId,
        int expectedNodeCount,
        int expectedEdgeCount)
    {
        await using var connection = await OpenAsync(databasePath);
        Assert.Equal(
            1L,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM graph_revisions WHERE revision = $revision AND run_id = $runId;",
                ("$revision", revision),
                ("$runId", runId)));
        Assert.Equal(
            (long)expectedNodeCount,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM graph_nodes WHERE revision = $revision;",
                ("$revision", revision)));
        Assert.Equal(
            (long)expectedEdgeCount,
            await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM graph_edges WHERE revision = $revision;",
                ("$revision", revision)));
    }

    public static async Task AssertActiveGraphRevisionAsync(
        string databasePath,
        string workspaceId,
        long? expectedRevision)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id = $workspaceId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The workspace graph state was not persisted.");
        Assert.Equal(expectedRevision, reader.IsDBNull(0) ? null : reader.GetInt64(0));
    }

    public static async Task AssertGraphEdgeAsync(
        string databasePath,
        long revision,
        string edgeId,
        string expectedJoinKey,
        string expectedEvidenceJson)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT normalized_join_key, evidence_json FROM graph_edges WHERE revision = $revision AND edge_id = $edgeId;";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$edgeId", edgeId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The graph edge was not persisted.");
        Assert.Equal(expectedJoinKey, reader.GetString(0));
        Assert.Equal(expectedEvidenceJson, reader.GetString(1));
    }

    public static async Task<long> CountAsync(string databasePath, string tableName)
    {
        await using var connection = await OpenAsync(databasePath);
        return await ScalarLongAsync(connection, $"SELECT COUNT(*) FROM {tableName};");
    }

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
