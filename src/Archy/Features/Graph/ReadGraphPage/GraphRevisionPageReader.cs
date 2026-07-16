using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Graph.ReadGraphPage;

/// <summary>Reads bounded node and edge pages without materializing an entire graph revision.</summary>
public sealed class GraphRevisionPageReader(IWorkspaceLockManager lockManager) : IGraphRevisionPageReader
{
    private const int MaximumLimit = 500;
    private const int MaximumOffset = 1_000_000;

    public async ValueTask<Result<GraphRevisionMetadata?>> ReadMetadataAsync(
        WorkspaceStateLocation location,
        long? revision,
        CancellationToken cancellationToken)
    {
        if (revision is < 1)
        {
            return ResultFactory.Failure<GraphRevisionMetadata?>(Problem.Validation("A graph revision must be a positive integer when supplied."));
        }

        return await WithConnectionAsync(location, async (connection, cancellationToken) =>
        {
            var resolved = await ResolveRevisionAsync(connection, location.WorkspaceId, revision, cancellationToken);
            if (resolved is null)
            {
                return revision is null
                    ? ResultFactory.Success<GraphRevisionMetadata?>(null)
                    : ResultFactory.Failure<GraphRevisionMetadata?>(Problem.NotFound($"Graph revision '{revision}' was not found in this workspace."));
            }

            var counts = await ReadCountsAsync(connection, resolved.Value, cancellationToken);
            return ResultFactory.Success<GraphRevisionMetadata?>(new GraphRevisionMetadata(resolved.Value, counts.NodeCount, counts.EdgeCount));
        }, cancellationToken);
    }

    public async ValueTask<Result<GraphRevisionPage>> ReadPageAsync(
        WorkspaceStateLocation location,
        GraphRevisionPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invalid = Validate(query);
        if (invalid is not null)
        {
            return ResultFactory.Failure<GraphRevisionPage>(invalid);
        }

        var result = await WithConnectionAsync(location, async (connection, cancellationToken) =>
        {
            var resolved = await ResolveRevisionAsync(connection, location.WorkspaceId, query.Revision, cancellationToken);
            if (resolved is null)
            {
                return ResultFactory.Failure<GraphRevisionPage>(Problem.NotFound(query.Revision is null
                    ? "The workspace has no committed graph revision."
                    : $"Graph revision '{query.Revision}' was not found in this workspace."));
            }

            return query.FactKind switch
            {
                GraphRevisionFactKind.Nodes => await ReadNodesPageAsync(connection, resolved.Value, query, cancellationToken),
                GraphRevisionFactKind.Edges => await ReadEdgesPageAsync(connection, resolved.Value, query, cancellationToken),
                _ => ResultFactory.Failure<GraphRevisionPage>(Problem.Validation("The requested graph fact collection is not supported.")),
            };
        }, cancellationToken);
        return result;
    }

    private async ValueTask<Result<T>> WithConnectionAsync<T>(
        WorkspaceStateLocation location,
        Func<SqliteConnection, CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<T>(lease.Problem!);
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
            return await operation(connection, cancellationToken);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<T>(Problem.Storage($"Archy could not read graph revision data: {exception.Message}"));
        }
    }

    private static Problem? Validate(GraphRevisionPageQuery query)
    {
        return query.Revision is < 1
               || !Enum.IsDefined(query.FactKind)
               || query.Offset is < 0 or > MaximumOffset
               || query.Limit is < 1 or > MaximumLimit
            ? Problem.Validation($"Graph pages require a valid fact collection, a positive revision when supplied, offset between 0 and {MaximumOffset}, and limit between 1 and {MaximumLimit}.")
            : null;
    }

    private static async Task<long?> ResolveRevisionAsync(SqliteConnection connection, string workspaceId, long? requestedRevision, CancellationToken cancellationToken)
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

    private static async Task<(int NodeCount, int EdgeCount)> ReadCountsAsync(SqliteConnection connection, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM graph_nodes WHERE revision = $revision), (SELECT COUNT(*) FROM graph_edges WHERE revision = $revision);";
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<Result<GraphRevisionPage>> ReadNodesPageAsync(SqliteConnection connection, long revision, GraphRevisionPageQuery query, CancellationToken cancellationToken)
    {
        var total = await CountAsync(connection, "graph_nodes", revision, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash FROM graph_nodes WHERE revision = $revision ORDER BY stable_id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var nodes = new List<GraphNodeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new GraphNodeFact(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7), reader.GetDouble(8), reader.GetString(9), reader.GetString(10)));
        }

        return ResultFactory.Success(new GraphRevisionPage(query.FactKind, revision, query.Offset, query.Limit, total, nodes, []));
    }

    private static async Task<Result<GraphRevisionPage>> ReadEdgesPageAsync(SqliteConnection connection, long revision, GraphRevisionPageQuery query, CancellationToken cancellationToken)
    {
        var total = await CountAsync(connection, "graph_edges", revision, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json FROM graph_edges WHERE revision = $revision ORDER BY edge_id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<GraphEdgeFact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new GraphEdgeFact(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetDouble(6), reader.GetString(7)));
        }

        return ResultFactory.Success(new GraphRevisionPage(query.FactKind, revision, query.Offset, query.Limit, total, [], edges));
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string tableName, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE revision = $revision;";
        command.Parameters.AddWithValue("$revision", revision);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
