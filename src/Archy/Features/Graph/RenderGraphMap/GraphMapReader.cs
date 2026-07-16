using System.Globalization;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Graph.RenderGraphMap;

/// <summary>
/// Reads a compact graph projection for the canvas. The browser receives identities and visual facts,
/// while detailed JSON evidence is requested only when someone selects an item.
/// </summary>
public sealed class GraphMapReader(IWorkspaceLockManager lockManager) : IGraphMapReader
{
    private const int MaximumNodes = 5_000;
    private const int MaximumEdges = 15_000;

    public async ValueTask<Result<GraphMapSnapshot>> ReadAsync(WorkspaceStateLocation location, long? revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (revision is < 1) return ResultFactory.Failure<GraphMapSnapshot>(Problem.Validation("A graph revision must be a positive integer when supplied."));

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(20), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<GraphMapSnapshot>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = location.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await connection.OpenAsync(cancellationToken);
            var resolvedRevision = await ResolveRevisionAsync(connection, location.WorkspaceId, revision, cancellationToken);
            if (resolvedRevision is null)
            {
                return ResultFactory.Failure<GraphMapSnapshot>(Problem.NotFound(revision is null ? "The workspace has no committed graph revision." : $"Graph revision '{revision}' was not found in this workspace."));
            }

            var counts = await ReadCountsAsync(connection, resolvedRevision.Value, cancellationToken);
            var nodes = await ReadNodesAsync(connection, resolvedRevision.Value, cancellationToken);
            var visibleIds = nodes.Select(static node => node.StableId).ToHashSet(StringComparer.Ordinal);
            var edges = (await ReadEdgesAsync(connection, resolvedRevision.Value, cancellationToken))
                .Where(edge => visibleIds.Contains(edge.SourceStableId) && visibleIds.Contains(edge.TargetStableId))
                .ToArray();
            return ResultFactory.Success(new GraphMapSnapshot(
                resolvedRevision.Value,
                counts.NodeCount,
                counts.EdgeCount,
                counts.NodeCount > nodes.Count || counts.EdgeCount > edges.Length,
                nodes,
                edges));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<GraphMapSnapshot>(Problem.Storage($"Archy could not prepare a graph map: {exception.Message}"));
        }
    }

    private static async Task<long?> ResolveRevisionAsync(SqliteConnection connection, string workspaceId, long? requestedRevision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = requestedRevision is null
            ? "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id = $workspaceId;"
            : "SELECT revision FROM graph_revisions WHERE workspace_id = $workspaceId AND revision = $revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        if (requestedRevision is not null) command.Parameters.AddWithValue("$revision", requestedRevision.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(int NodeCount, int EdgeCount)> ReadCountsAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM graph_nodes WHERE revision = $revision), (SELECT COUNT(*) FROM graph_edges WHERE revision = $revision);";
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<IReadOnlyList<GraphMapNode>> ReadNodesAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stable_id, node_kind, canonical_key, display_name, file_path, provider, confidence FROM graph_nodes WHERE revision = $revision ORDER BY COALESCE(file_path, ''), display_name, stable_id LIMIT $limit;";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", MaximumNodes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var nodes = new List<GraphMapNode>();
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new GraphMapNode(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetDouble(6)));
        }
        return nodes;
    }

    private static async Task<IReadOnlyList<GraphMapEdge>> ReadEdgesAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT edge_id, source_stable_id, target_stable_id, edge_kind, confidence FROM graph_edges WHERE revision = $revision ORDER BY confidence DESC, edge_id LIMIT $limit;";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", MaximumEdges);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<GraphMapEdge>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new GraphMapEdge(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
        }
        return edges;
    }
}
