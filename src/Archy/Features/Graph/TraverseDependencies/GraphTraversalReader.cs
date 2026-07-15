using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.TraverseDependencies;

public sealed class GraphTraversalReader(IWorkspaceLockManager lockManager) : IGraphTraversalReader
{
    private const int MaximumDepth = 6;
    private const int MaximumEdges = 500;

    public async ValueTask<Result<GraphTraversal>> TraverseAsync(
        WorkspaceStateLocation location,
        GraphTraversalQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(query);
        var invalid = Validate(query);
        if (invalid is not null)
        {
            return ResultFactory.Failure<GraphTraversal>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<GraphTraversal>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var revision = await ResolveRevisionAsync(connection, location.WorkspaceId, query.Revision, cancellationToken);
            if (revision is null)
            {
                return ResultFactory.Failure<GraphTraversal>(
                    Problem.NotFound(query.Revision is null
                        ? "The workspace has no committed graph revision."
                        : $"Graph revision '{query.Revision}' was not found in this workspace."));
            }

            if (!await NodeExistsAsync(connection, revision.Value, query.StartStableId, cancellationToken))
            {
                return ResultFactory.Failure<GraphTraversal>(
                    Problem.NotFound($"Graph node '{query.StartStableId}' was not found at revision '{revision.Value}'."));
            }

            var edges = await ReadTraversalEdgesAsync(connection, revision.Value, query, cancellationToken);
            var isTruncated = edges.Count > query.MaxEdges;
            if (isTruncated)
            {
                edges.RemoveAt(edges.Count - 1);
            }

            return ResultFactory.Success(new GraphTraversal(
                revision.Value,
                query.StartStableId,
                query.Direction,
                [.. edges],
                isTruncated));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<GraphTraversal>(
                Problem.Storage($"Archy could not traverse graph dependencies: {exception.Message}"));
        }
    }

    private static Problem? Validate(GraphTraversalQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.StartStableId) ||
            !Enum.IsDefined(query.Direction) ||
            query.Revision is < 1 ||
            query.MaxDepth is < 1 or > MaximumDepth ||
            query.MaxEdges is < 1 or > MaximumEdges)
        {
            return Problem.Validation($"Graph traversal requires a start node, a positive revision when supplied, depth between 1 and {MaximumDepth}, and at most {MaximumEdges} edges.");
        }

        return null;
    }

    private static async Task<long?> ResolveRevisionAsync(
        SqliteConnection connection,
        string workspaceId,
        long? requestedRevision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = requestedRevision is null
            ? "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id = $workspaceId;"
            : "SELECT revision FROM graph_revisions WHERE workspace_id = $workspaceId AND revision = $revision;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        if (requestedRevision is not null)
        {
            command.Parameters.AddWithValue("$revision", requestedRevision.Value);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> NodeExistsAsync(
        SqliteConnection connection,
        long revision,
        string stableId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM graph_nodes WHERE revision = $revision AND stable_id = $stableId);";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$stableId", stableId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<List<TraversedGraphEdge>> ReadTraversalEdgesAsync(
        SqliteConnection connection,
        long revision,
        GraphTraversalQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query.Direction == GraphTraversalDirection.Dependencies
            ? DependenciesSql
            : DependentsSql;
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$startStableId", query.StartStableId);
        command.Parameters.AddWithValue("$maxDepth", query.MaxDepth);
        command.Parameters.AddWithValue("$limit", query.MaxEdges + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<TraversedGraphEdge>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new TraversedGraphEdge(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8)));
        }

        return edges;
    }

    private const string DependenciesSql = """
        WITH RECURSIVE traversal(depth, reached_stable_id, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json, visited) AS (
            SELECT
                1,
                edge.target_stable_id,
                edge.edge_id,
                edge.source_stable_id,
                edge.target_stable_id,
                edge.edge_kind,
                edge.normalized_join_key,
                edge.provider,
                edge.confidence,
                edge.evidence_json,
                quote($startStableId) || quote(edge.target_stable_id)
            FROM graph_edges edge
            WHERE edge.revision = $revision
              AND edge.source_stable_id = $startStableId
              AND instr(quote($startStableId), quote(edge.target_stable_id)) = 0

            UNION ALL

            SELECT
                traversal.depth + 1,
                edge.target_stable_id,
                edge.edge_id,
                edge.source_stable_id,
                edge.target_stable_id,
                edge.edge_kind,
                edge.normalized_join_key,
                edge.provider,
                edge.confidence,
                edge.evidence_json,
                traversal.visited || quote(edge.target_stable_id)
            FROM graph_edges edge
            INNER JOIN traversal ON edge.source_stable_id = traversal.reached_stable_id
            WHERE edge.revision = $revision
              AND traversal.depth < $maxDepth
              AND instr(traversal.visited, quote(edge.target_stable_id)) = 0
        )
        SELECT
            MIN(depth) AS depth,
            edge_id,
            source_stable_id,
            target_stable_id,
            edge_kind,
            normalized_join_key,
            provider,
            confidence,
            evidence_json
        FROM traversal
        GROUP BY edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json
        ORDER BY depth, edge_id
        LIMIT $limit;
        """;

    private const string DependentsSql = """
        WITH RECURSIVE traversal(depth, reached_stable_id, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json, visited) AS (
            SELECT
                1,
                edge.source_stable_id,
                edge.edge_id,
                edge.source_stable_id,
                edge.target_stable_id,
                edge.edge_kind,
                edge.normalized_join_key,
                edge.provider,
                edge.confidence,
                edge.evidence_json,
                quote($startStableId) || quote(edge.source_stable_id)
            FROM graph_edges edge
            WHERE edge.revision = $revision
              AND edge.target_stable_id = $startStableId
              AND instr(quote($startStableId), quote(edge.source_stable_id)) = 0

            UNION ALL

            SELECT
                traversal.depth + 1,
                edge.source_stable_id,
                edge.edge_id,
                edge.source_stable_id,
                edge.target_stable_id,
                edge.edge_kind,
                edge.normalized_join_key,
                edge.provider,
                edge.confidence,
                edge.evidence_json,
                traversal.visited || quote(edge.source_stable_id)
            FROM graph_edges edge
            INNER JOIN traversal ON edge.target_stable_id = traversal.reached_stable_id
            WHERE edge.revision = $revision
              AND traversal.depth < $maxDepth
              AND instr(traversal.visited, quote(edge.source_stable_id)) = 0
        )
        SELECT
            MIN(depth) AS depth,
            edge_id,
            source_stable_id,
            target_stable_id,
            edge_kind,
            normalized_join_key,
            provider,
            confidence,
            evidence_json
        FROM traversal
        GROUP BY edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json
        ORDER BY depth, edge_id
        LIMIT $limit;
        """;
}
