using Microsoft.Data.Sqlite;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class GraphVersionReader
{
    public static Task<IReadOnlyList<NodeVersion>> ReadNodeVersionsAsync(
        string databasePath,
        string workspaceId,
        string stableId) =>
        ReadNodeVersionsCoreAsync(databasePath, workspaceId, stableId);

    public static Task<IReadOnlyList<EdgeVersion>> ReadEdgeVersionsAsync(
        string databasePath,
        string workspaceId,
        string edgeId) =>
        ReadEdgeVersionsCoreAsync(databasePath, workspaceId, edgeId);

    public static Task<IReadOnlyList<SymbolVersion>> ReadSymbolVersionsAsync(
        string databasePath,
        string workspaceId,
        string symbolId) =>
        ReadSymbolVersionsCoreAsync(databasePath, workspaceId, symbolId);

    public static Task<IReadOnlyList<InterfaceFingerprintVersion>> ReadInterfaceFingerprintVersionsAsync(
        string databasePath,
        string workspaceId,
        string symbolId,
        string fingerprintKind) =>
        ReadInterfaceFingerprintVersionsCoreAsync(databasePath, workspaceId, symbolId, fingerprintKind);

    private static async Task<IReadOnlyList<NodeVersion>> ReadNodeVersionsCoreAsync(
        string databasePath,
        string workspaceId,
        string stableId)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT valid_from_revision, valid_to_revision, content_hash FROM graph_node_versions WHERE workspace_id = $workspaceId AND stable_id = $stableId ORDER BY valid_from_revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$stableId", stableId);
        await using var reader = await command.ExecuteReaderAsync();
        var versions = new List<NodeVersion>();
        while (await reader.ReadAsync())
        {
            versions.Add(new NodeVersion(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2)));
        }

        return versions;
    }

    private static async Task<IReadOnlyList<EdgeVersion>> ReadEdgeVersionsCoreAsync(
        string databasePath,
        string workspaceId,
        string edgeId)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT valid_from_revision, valid_to_revision FROM graph_edge_versions WHERE workspace_id = $workspaceId AND edge_id = $edgeId ORDER BY valid_from_revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$edgeId", edgeId);
        await using var reader = await command.ExecuteReaderAsync();
        var versions = new List<EdgeVersion>();
        while (await reader.ReadAsync())
        {
            versions.Add(new EdgeVersion(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1)));
        }

        return versions;
    }

    private static async Task<IReadOnlyList<SymbolVersion>> ReadSymbolVersionsCoreAsync(
        string databasePath,
        string workspaceId,
        string symbolId)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT valid_from_revision, valid_to_revision, signature_hash FROM symbol_versions WHERE workspace_id = $workspaceId AND symbol_id = $symbolId ORDER BY valid_from_revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$symbolId", symbolId);
        await using var reader = await command.ExecuteReaderAsync();
        var versions = new List<SymbolVersion>();
        while (await reader.ReadAsync())
        {
            versions.Add(new SymbolVersion(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2)));
        }

        return versions;
    }

    private static async Task<IReadOnlyList<InterfaceFingerprintVersion>> ReadInterfaceFingerprintVersionsCoreAsync(
        string databasePath,
        string workspaceId,
        string symbolId,
        string fingerprintKind)
    {
        await using var connection = await OpenAsync(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT valid_from_revision, valid_to_revision, fingerprint_hash FROM interface_fingerprint_versions WHERE workspace_id = $workspaceId AND symbol_id = $symbolId AND fingerprint_kind = $fingerprintKind ORDER BY valid_from_revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$symbolId", symbolId);
        command.Parameters.AddWithValue("$fingerprintKind", fingerprintKind);
        await using var reader = await command.ExecuteReaderAsync();
        var versions = new List<InterfaceFingerprintVersion>();
        while (await reader.ReadAsync())
        {
            versions.Add(new InterfaceFingerprintVersion(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2)));
        }

        return versions;
    }

    private static async Task<SqliteConnection> OpenAsync(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return connection;
    }

    internal sealed record NodeVersion(long ValidFromRevision, long? ValidToRevision, string ContentHash);

    internal sealed record EdgeVersion(long ValidFromRevision, long? ValidToRevision);

    internal sealed record SymbolVersion(long ValidFromRevision, long? ValidToRevision, string SignatureHash);

    internal sealed record InterfaceFingerprintVersion(long ValidFromRevision, long? ValidToRevision, string FingerprintHash);
}
