using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Graph.ReadGraphRevision;

/// <summary>Reads a complete active revision while holding one shared workspace read lock.</summary>
public sealed class GraphRevisionSnapshotReader(IWorkspaceLockManager lockManager) : IGraphRevisionSnapshotReader
{
    public async ValueTask<Result<GraphRevisionSnapshot?>> ReadActiveAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Read,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<GraphRevisionSnapshot?>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = location.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            var revision = await ReadActiveRevisionAsync(connection, location.WorkspaceId, cancellationToken);
            if (revision is null)
            {
                return ResultFactory.Success<GraphRevisionSnapshot?>(null);
            }

            var nodes = await ReadNodesAsync(connection, revision.Value, cancellationToken);
            var edges = await ReadEdgesAsync(connection, revision.Value, cancellationToken);
            var symbols = await ReadSymbolsAsync(connection, location.WorkspaceId, revision.Value, cancellationToken);
            var interfaceFingerprints = await ReadInterfaceFingerprintsAsync(connection, location.WorkspaceId, revision.Value, cancellationToken);
            return ResultFactory.Success<GraphRevisionSnapshot?>(new GraphRevisionSnapshot(
                revision.Value,
                nodes,
                edges,
                symbols,
                interfaceFingerprints));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<GraphRevisionSnapshot?>(
                Problem.Storage($"Archy could not read the active graph revision snapshot: {exception.Message}"));
        }
    }

    private static async Task<long?> ReadActiveRevisionAsync(SqliteConnection connection, string workspaceId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id = $workspaceId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<GraphNodeFact>> ReadNodesAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash FROM graph_nodes WHERE revision = $revision ORDER BY stable_id;";
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var nodes = new List<GraphNodeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new GraphNodeFact(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7),
                reader.GetDouble(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return nodes;
    }

    private static async Task<IReadOnlyList<GraphEdgeFact>> ReadEdgesAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json FROM graph_edges WHERE revision = $revision ORDER BY edge_id;";
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<GraphEdgeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new GraphEdgeFact(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetDouble(6),
                reader.GetString(7)));
        }

        return edges;
    }

    private static async Task<IReadOnlyList<GraphSymbolFact>> ReadSymbolsAsync(
        SqliteConnection connection,
        string workspaceId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT symbol_id, node_stable_id, fully_qualified_name, visibility, normalized_signature, parameter_metadata_json, return_metadata_json, signature_hash
            FROM symbol_versions
            WHERE workspace_id = $workspaceId
              AND valid_from_revision <= $revision
              AND (valid_to_revision IS NULL OR valid_to_revision >= $revision)
            ORDER BY symbol_id;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var symbols = new List<GraphSymbolFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            symbols.Add(new GraphSymbolFact(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return symbols;
    }

    private static async Task<IReadOnlyList<InterfaceFingerprintFact>> ReadInterfaceFingerprintsAsync(
        SqliteConnection connection,
        string workspaceId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT symbol_id, fingerprint_kind, fingerprint_hash, normalized_members_json
            FROM interface_fingerprint_versions
            WHERE workspace_id = $workspaceId
              AND valid_from_revision <= $revision
              AND (valid_to_revision IS NULL OR valid_to_revision >= $revision)
            ORDER BY symbol_id, fingerprint_kind;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var interfaceFingerprints = new List<InterfaceFingerprintFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            interfaceFingerprints.Add(new InterfaceFingerprintFact(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return interfaceFingerprints;
    }
}
