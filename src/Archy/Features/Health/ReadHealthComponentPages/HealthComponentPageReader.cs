using Archy.Features.Health.HealthSnapshots;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Health.ReadHealthComponentPages;

public sealed class HealthComponentPageReader(IWorkspaceLockManager lockManager) : IHealthComponentPageReader
{
    public async ValueTask<Result<HealthComponentPage>> ReadAsync(WorkspaceStateLocation location,string snapshotId,int offset,int limit,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);if(string.IsNullOrWhiteSpace(snapshotId)||offset is<0 or>1_000_000||limit is<1 or>100)return ResultFactory.Failure<HealthComponentPage>(Problem.Validation("Health component pages require an ID, offset between 0 and 1000000, and limit between 1 and 100."));
        var lease=await lockManager.AcquireAsync(location,WorkspaceLockMode.Read,TimeSpan.FromSeconds(30),cancellationToken);if(!lease.IsSuccess)return ResultFactory.Failure<HealthComponentPage>(lease.Problem!);using var held=lease.Value;
        try { SQLitePCL.Batteries_V2.Init();await using var c=new SqliteConnection($"Data Source={location.DatabasePath}");await c.OpenAsync(cancellationToken);var metadata=await MetadataAsync(c,location.WorkspaceId,snapshotId,cancellationToken);if(metadata is null)return ResultFactory.Failure<HealthComponentPage>(Problem.NotFound($"Health snapshot '{snapshotId}' was not found."));var total=await CountAsync(c,snapshotId,cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT component_key,raw_value,weight,weighted_contribution,detail_json FROM health_metric_components WHERE health_snapshot_id=$id ORDER BY component_key LIMIT $limit OFFSET $offset;";q.Parameters.AddWithValue("$id",snapshotId);q.Parameters.AddWithValue("$limit",limit);q.Parameters.AddWithValue("$offset",offset);await using var r=await q.ExecuteReaderAsync(cancellationToken);var items=new List<HealthMetricComponent>();while(await r.ReadAsync(cancellationToken))items.Add(new HealthMetricComponent(r.GetString(0),r.GetDouble(1),r.GetDouble(2),r.GetDouble(3),r.GetString(4)));return ResultFactory.Success(new HealthComponentPage(snapshotId,metadata.Value.Revision,metadata.Value.Version,metadata.Value.Score,offset,limit,total,[..items]));}catch(SqliteException e){return ResultFactory.Failure<HealthComponentPage>(Problem.Storage($"Archy could not read health components: {e.Message}"));}
    }
    private static async Task<(long Revision,string Version,double Score)?> MetadataAsync(SqliteConnection c,string workspace,string id,CancellationToken ct){await using var q=c.CreateCommand();q.CommandText="SELECT graph_revision,calculation_version,score FROM health_snapshots WHERE workspace_id=$workspace AND health_snapshot_id=$id;";q.Parameters.AddWithValue("$workspace",workspace);q.Parameters.AddWithValue("$id",id);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetInt64(0),r.GetString(1),r.GetDouble(2)):null;}
    private static async Task<int> CountAsync(SqliteConnection c,string id,CancellationToken ct){await using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM health_metric_components WHERE health_snapshot_id=$id;";q.Parameters.AddWithValue("$id",id);return Convert.ToInt32(await q.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture);}
}
