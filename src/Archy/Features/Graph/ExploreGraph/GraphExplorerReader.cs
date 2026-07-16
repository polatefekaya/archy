using System.Globalization;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Graph.ExploreGraph;

/// <summary>
/// Creates a bounded connected neighbourhood using local SQLite traversal. It intentionally avoids
/// pairing unrelated storage pages, which is visually misleading and frequently renders zero edges.
/// </summary>
public sealed class GraphExplorerReader(IWorkspaceLockManager lockManager) : IGraphExplorerReader
{
    private const int DefaultMaximumNodes = 72;
    private const int MaximumNodes = 120;
    private const int MaximumEdgesPerNode = 18;
    private const int MaximumSearchResults = 16;

    public async ValueTask<Result<GraphExplorerSnapshot>> ReadAsync(
        WorkspaceStateLocation location,
        GraphExplorerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Revision is < 1 || request.MaximumNodes is < 1 or > MaximumNodes)
        {
            return ResultFactory.Failure<GraphExplorerSnapshot>(Problem.Validation($"Graph exploration requires a positive revision when supplied and between 1 and {MaximumNodes} nodes."));
        }

        return await WithConnectionAsync(location, async (connection, cancellationToken) =>
        {
            var revision = await ResolveRevisionAsync(connection, location.WorkspaceId, request.Revision, cancellationToken);
            if (revision is null)
            {
                return ResultFactory.Failure<GraphExplorerSnapshot>(Problem.NotFound(request.Revision is null
                    ? "The workspace has no committed graph revision."
                    : $"Graph revision '{request.Revision}' was not found in this workspace."));
            }

            var counts = await ReadCountsAsync(connection, revision.Value, cancellationToken);
            if (counts.NodeCount == 0)
            {
                return ResultFactory.Success(new GraphExplorerSnapshot(revision.Value, 0, counts.EdgeCount, string.Empty, request.FocusStableId is not null, [], []));
            }

            var center = request.FocusStableId;
            if (string.IsNullOrWhiteSpace(center))
            {
                center = await FindHubAsync(connection, revision.Value, cancellationToken)
                    ?? await FindFirstNodeAsync(connection, revision.Value, cancellationToken);
            }
            else if (!await NodeExistsAsync(connection, revision.Value, center, cancellationToken))
            {
                return ResultFactory.Failure<GraphExplorerSnapshot>(Problem.NotFound("The requested graph node was not found at this revision."));
            }

            var maximumNodes = request.MaximumNodes == 0 ? DefaultMaximumNodes : request.MaximumNodes;
            var nodeIds = new HashSet<string>(StringComparer.Ordinal) { center! };
            var queued = new HashSet<string>(StringComparer.Ordinal) { center! };
            var frontier = new Queue<string>();
            frontier.Enqueue(center!);
            var edges = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);

            while (frontier.Count > 0 && nodeIds.Count < maximumNodes)
            {
                var current = frontier.Dequeue();
                var adjacent = await ReadAdjacentEdgesAsync(connection, revision.Value, current, cancellationToken);
                foreach (var edge in adjacent)
                {
                    var neighbour = string.Equals(edge.SourceStableId, current, StringComparison.Ordinal)
                        ? edge.TargetStableId
                        : edge.SourceStableId;
                    if (!nodeIds.Contains(neighbour) && nodeIds.Count >= maximumNodes)
                    {
                        continue;
                    }

                    edges[edge.EdgeId] = edge;
                    nodeIds.Add(neighbour);
                    if (queued.Add(neighbour))
                    {
                        frontier.Enqueue(neighbour);
                    }
                }
            }

            var nodes = await ReadNodesAsync(connection, revision.Value, nodeIds, cancellationToken);
            var availableIds = nodes.Select(static node => node.StableId).ToHashSet(StringComparer.Ordinal);
            var connectedEdges = edges.Values
                .Where(edge => availableIds.Contains(edge.SourceStableId) && availableIds.Contains(edge.TargetStableId))
                .OrderByDescending(static edge => edge.Confidence)
                .ThenBy(static edge => edge.EdgeId, StringComparer.Ordinal)
                .ToArray();
            return ResultFactory.Success(new GraphExplorerSnapshot(
                revision.Value,
                counts.NodeCount,
                counts.EdgeCount,
                center!,
                request.FocusStableId is not null,
                nodes,
                connectedEdges));
        }, cancellationToken);
    }

    public async ValueTask<Result<IReadOnlyList<GraphNodeFact>>> SearchAsync(
        WorkspaceStateLocation location,
        string query,
        long? revision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (revision is < 1 || string.IsNullOrWhiteSpace(query) || query.Length > 160)
        {
            return ResultFactory.Failure<IReadOnlyList<GraphNodeFact>>(Problem.Validation("Graph search requires between 1 and 160 non-whitespace characters and a positive revision when supplied."));
        }

        return await WithConnectionAsync(location, async (connection, cancellationToken) =>
        {
            var resolved = await ResolveRevisionAsync(connection, location.WorkspaceId, revision, cancellationToken);
            if (resolved is null)
            {
                return ResultFactory.Failure<IReadOnlyList<GraphNodeFact>>(Problem.NotFound("The requested graph revision was not found in this workspace."));
            }

            var pattern = $"%{EscapeLike(query.Trim())}%";
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash
                FROM graph_nodes
                WHERE revision = $revision
                  AND (display_name LIKE $pattern ESCAPE '!' OR canonical_key LIKE $pattern ESCAPE '!' OR file_path LIKE $pattern ESCAPE '!')
                ORDER BY CASE WHEN display_name = $exact THEN 0 WHEN display_name LIKE $prefix ESCAPE '!' THEN 1 ELSE 2 END, display_name, stable_id
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$revision", resolved.Value);
            command.Parameters.AddWithValue("$pattern", pattern);
            command.Parameters.AddWithValue("$exact", query.Trim());
            command.Parameters.AddWithValue("$prefix", $"{EscapeLike(query.Trim())}%");
            command.Parameters.AddWithValue("$limit", MaximumSearchResults);
            return ResultFactory.Success<IReadOnlyList<GraphNodeFact>>(await ReadNodesAsync(command, cancellationToken));
        }, cancellationToken);
    }

    private async ValueTask<Result<T>> WithConnectionAsync<T>(WorkspaceStateLocation location, Func<SqliteConnection, CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken)
    {
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(15), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<T>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = location.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await connection.OpenAsync(cancellationToken);
            return await operation(connection, cancellationToken);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<T>(Problem.Storage($"Archy could not explore graph data: {exception.Message}"));
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

    private static async Task<string?> FindHubAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT stable_id FROM (
                SELECT source_stable_id AS stable_id, COUNT(*) AS degree FROM graph_edges WHERE revision = $revision GROUP BY source_stable_id
                UNION ALL
                SELECT target_stable_id AS stable_id, COUNT(*) AS degree FROM graph_edges WHERE revision = $revision GROUP BY target_stable_id
            ) GROUP BY stable_id ORDER BY SUM(degree) DESC, stable_id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revision);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<string?> FindFirstNodeAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stable_id FROM graph_nodes WHERE revision = $revision ORDER BY stable_id LIMIT 1;";
        command.Parameters.AddWithValue("$revision", revision);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<bool> NodeExistsAsync(SqliteConnection connection, long revision, string stableId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM graph_nodes WHERE revision = $revision AND stable_id = $stableId);";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$stableId", stableId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<IReadOnlyList<GraphEdgeFact>> ReadAdjacentEdgesAsync(SqliteConnection connection, long revision, string stableId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json
            FROM graph_edges
            WHERE revision = $revision AND (source_stable_id = $stableId OR target_stable_id = $stableId)
            ORDER BY confidence DESC, edge_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$stableId", stableId);
        command.Parameters.AddWithValue("$limit", MaximumEdgesPerNode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<GraphEdgeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new GraphEdgeFact(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetDouble(6), reader.GetString(7)));
        }
        return edges;
    }

    private static async Task<IReadOnlyList<GraphNodeFact>> ReadNodesAsync(SqliteConnection connection, long revision, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var names = ids.OrderBy(static id => id, StringComparer.Ordinal).Select((_, index) => $"$id{index}").ToArray();
        command.CommandText = $"SELECT stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash FROM graph_nodes WHERE revision = $revision AND stable_id IN ({string.Join(',', names)}) ORDER BY display_name, stable_id;";
        command.Parameters.AddWithValue("$revision", revision);
        foreach (var (id, index) in ids.OrderBy(static id => id, StringComparer.Ordinal).Select((id, index) => (id, index))) command.Parameters.AddWithValue($"$id{index}", id);
        return await ReadNodesAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<GraphNodeFact>> ReadNodesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var nodes = new List<GraphNodeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new GraphNodeFact(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7), reader.GetDouble(8), reader.GetString(9), reader.GetString(10)));
        }
        return nodes;
    }

    private static string EscapeLike(string value) => value.Replace("!", "!!", StringComparison.Ordinal).Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal);
}
