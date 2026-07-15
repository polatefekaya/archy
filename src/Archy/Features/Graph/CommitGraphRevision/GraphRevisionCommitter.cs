using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

public sealed class GraphRevisionCommitter(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IGraphRevisionCommitter
{
    public ValueTask<Result<CommittedGraphRevision>> CommitAsync(
        WorkspaceStateLocation location,
        string runId,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        CancellationToken cancellationToken) =>
        CommitAsync(location, runId, nodes, edges, [], [], cancellationToken);

    public async ValueTask<Result<CommittedGraphRevision>> CommitAsync(
        WorkspaceStateLocation location,
        string runId,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<GraphSymbolFact> symbols,
        IReadOnlyList<InterfaceFingerprintFact> interfaceFingerprints,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(interfaceFingerprints);

        var invalid = GraphFactValidator.Validate(nodes, edges, symbols, interfaceFingerprints);
        if (invalid is not null)
        {
            return ResultFactory.Failure<CommittedGraphRevision>(invalid);
        }

        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<CommittedGraphRevision>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var workspaceId = await GraphSql.ScalarStringAsync(
                connection,
                transaction,
                "SELECT workspace_id FROM analysis_runs WHERE run_id = $runId AND status = 'running';",
                cancellationToken,
                ("$runId", runId));
            if (workspaceId is null || !string.Equals(workspaceId, location.WorkspaceId, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<CommittedGraphRevision>(
                    Problem.Conflict($"Analysis run '{runId}' is not running for this workspace."));
            }

            var committedAt = timeProvider.GetUtcNow();
            await GraphSql.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO graph_revisions(workspace_id, run_id, committed_at_utc) VALUES ($workspaceId, $runId, $committedAt);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$runId", runId),
                ("$committedAt", committedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            var revision = await GraphSql.ScalarLongAsync(
                connection,
                transaction,
                "SELECT last_insert_rowid();",
                cancellationToken);

            var versioningProblem = await new GraphVersioningWriter(
                connection,
                transaction,
                location.WorkspaceId,
                runId,
                revision).PersistAsync(nodes, edges, symbols, interfaceFingerprints, cancellationToken);
            if (versioningProblem is not null)
            {
                return ResultFactory.Failure<CommittedGraphRevision>(versioningProblem);
            }

            await GraphSql.ExecuteAsync(
                connection,
                transaction,
                "UPDATE analysis_runs SET status = 'succeeded', completed_at_utc = $completedAt, graph_revision = $revision WHERE run_id = $runId AND status = 'running';",
                cancellationToken,
                ("$completedAt", committedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ("$revision", revision),
                ("$runId", runId));
            await GraphSql.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO analysis_run_events(run_id, event_type, occurred_at_utc, graph_revision) VALUES ($runId, 'succeeded', $occurredAt, $revision);",
                cancellationToken,
                ("$runId", runId),
                ("$occurredAt", committedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ("$revision", revision));
            var integrityProblem = await GraphRevisionIntegrityValidator.ValidateAsync(
                connection,
                transaction,
                location.WorkspaceId,
                revision,
                cancellationToken);
            if (integrityProblem is not null)
            {
                return ResultFactory.Failure<CommittedGraphRevision>(integrityProblem);
            }

            await GraphSql.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO workspace_graph_states(workspace_id, active_graph_revision, activated_at_utc) VALUES ($workspaceId, $revision, $activatedAt) ON CONFLICT(workspace_id) DO UPDATE SET active_graph_revision = excluded.active_graph_revision, activated_at_utc = excluded.activated_at_utc;",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$revision", revision),
                ("$activatedAt", committedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(cancellationToken);

            return ResultFactory.Success(
                new CommittedGraphRevision(
                    revision,
                    runId,
                    committedAt,
                    nodes.Count,
                    edges.Count,
                    symbols.Count,
                    interfaceFingerprints.Count));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<CommittedGraphRevision>(
                Problem.Storage($"Archy could not commit graph revision: {exception.Message}"));
        }
    }
}
