using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.AnalysisRuns;

public sealed class AnalysisRunStore(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IAnalysisRunStore
{
    public async ValueTask<Result<AnalysisRun>> StartAsync(WorkspaceStateLocation location, string analyzerVersion, string configurationHash, string? repositoryCommit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationHash);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<AnalysisRun>(lease.Problem!);
        using var heldLease = lease.Value;
        var run = new AnalysisRun(Guid.NewGuid().ToString("N"), location.WorkspaceId, analyzerVersion, configurationHash, repositoryCommit, AnalysisRunStatus.Running, timeProvider.GetUtcNow(), null, null);
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, "INSERT INTO analysis_runs(run_id, workspace_id, analyzer_version, configuration_hash, repository_commit, status, started_at_utc, completed_at_utc, graph_revision) VALUES ($id,$workspace,$version,$hash,$commit,'running',$started,NULL,NULL);", cancellationToken, ("$id",run.RunId),("$workspace",run.WorkspaceId),("$version",run.AnalyzerVersion),("$hash",run.ConfigurationHash),("$commit",run.RepositoryCommit),("$started",run.StartedAtUtc.ToString("O")));
            await ExecuteAsync(connection, transaction, "INSERT INTO analysis_run_events(run_id, event_type, occurred_at_utc, graph_revision) VALUES ($id,'started',$at,NULL);", cancellationToken, ("$id",run.RunId),("$at",run.StartedAtUtc.ToString("O")));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(run);
        }
        catch (SqliteException exception) { return ResultFactory.Failure<AnalysisRun>(Problem.Storage($"Archy could not start the analysis run: {exception.Message}")); }
    }

    public async ValueTask<Result<AnalysisRun>> CompleteAsync(WorkspaceStateLocation location, string runId, AnalysisRunStatus terminalStatus, long? graphRevision, CancellationToken cancellationToken)
    {
        if (terminalStatus is AnalysisRunStatus.Running or AnalysisRunStatus.Succeeded) return ResultFactory.Failure<AnalysisRun>(Problem.Validation("Succeeded runs are committed only through an immutable graph revision."));
        if (graphRevision is not null) return ResultFactory.Failure<AnalysisRun>(Problem.Validation("Only a successful immutable graph revision may reference a graph revision."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<AnalysisRun>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken);
            var completedAt = timeProvider.GetUtcNow(); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var existing = await LoadRunningAsync(connection, transaction, runId, cancellationToken);
            if (existing is null) return ResultFactory.Failure<AnalysisRun>(Problem.Conflict($"Analysis run '{runId}' is not running or does not exist."));
            var changed = await ExecuteAsync(connection, transaction, "UPDATE analysis_runs SET status=$status, completed_at_utc=$at, graph_revision=$revision WHERE run_id=$id AND status='running';", cancellationToken, ("$status", ToDatabase(terminalStatus)), ("$at",completedAt.ToString("O")), ("$revision",graphRevision?.ToString()), ("$id",runId));
            if (changed == 0) return ResultFactory.Failure<AnalysisRun>(Problem.Conflict($"Analysis run '{runId}' could not be completed."));
            await ExecuteAsync(connection, transaction, "INSERT INTO analysis_run_events(run_id, event_type, occurred_at_utc, graph_revision) VALUES ($id,$event,$at,$revision);", cancellationToken, ("$id",runId),("$event",ToDatabase(terminalStatus)),("$at",completedAt.ToString("O")),("$revision",graphRevision?.ToString()));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(existing with { Status = terminalStatus, CompletedAtUtc = completedAt, GraphRevision = graphRevision });
        }
        catch (SqliteException exception) { return ResultFactory.Failure<AnalysisRun>(Problem.Storage($"Archy could not complete the analysis run: {exception.Message}")); }
    }

    private static async Task<int> ExecuteAsync(SqliteConnection c, SqliteTransaction t, string sql, CancellationToken ct, params (string Name,string? Value)[] p) { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText=sql; foreach(var x in p) cmd.Parameters.AddWithValue(x.Name,(object?)x.Value??DBNull.Value); return await cmd.ExecuteNonQueryAsync(ct); }
    private static async Task<AnalysisRun?> LoadRunningAsync(SqliteConnection c, SqliteTransaction t, string id, CancellationToken ct) { await using var cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT workspace_id, analyzer_version, configuration_hash, repository_commit, started_at_utc FROM analysis_runs WHERE run_id=$id AND status='running';"; cmd.Parameters.AddWithValue("$id",id); await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct)) return null; return new AnalysisRun(id,r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),AnalysisRunStatus.Running,DateTimeOffset.Parse(r.GetString(4),System.Globalization.CultureInfo.InvariantCulture),null,null); }
    private static string ToDatabase(AnalysisRunStatus s) => s.ToString().ToLowerInvariant();
}
