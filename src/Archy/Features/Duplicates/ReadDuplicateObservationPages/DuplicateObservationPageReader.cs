using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Duplicates.ReadDuplicateObservationPages;

/// <summary>Pages duplicate observations; detailed evidence remains a separately bounded resource.</summary>
public sealed class DuplicateObservationPageReader(IWorkspaceLockManager lockManager) : IDuplicateObservationPageReader
{
    public async ValueTask<Result<DuplicateObservationPage>> ReadAsync(WorkspaceStateLocation location, string findingId, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(findingId) || offset is < 0 or > 1_000_000 || limit is < 1 or > 100) return ResultFactory.Failure<DuplicateObservationPage>(Problem.Validation("Duplicate observation pages require an ID, offset between 0 and 1000000, and limit between 1 and 100."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<DuplicateObservationPage>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken);
            var identity = await IdentityAsync(connection, location.WorkspaceId, findingId, cancellationToken); if (identity is null) return ResultFactory.Failure<DuplicateObservationPage>(Problem.NotFound($"Duplicate finding '{findingId}' was not found."));
            var total = await CountAsync(connection, location.WorkspaceId, findingId, cancellationToken);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT finding_observation_id, graph_revision, aggregation_version, confidence, rationale_json, observed_at_utc FROM duplicate_finding_observations WHERE workspace_id=$workspaceId AND finding_id=$findingId ORDER BY graph_revision, observed_at_utc, finding_observation_id LIMIT $limit OFFSET $offset;"; command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId); command.Parameters.AddWithValue("$findingId", findingId); command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); var items = new List<DuplicateObservationPageItem>(); while (await reader.ReadAsync(cancellationToken)) items.Add(new DuplicateObservationPageItem(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetDouble(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture)));
            return ResultFactory.Success(new DuplicateObservationPage(findingId, identity.Value.Left, identity.Value.Right, offset, limit, total, [.. items]));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<DuplicateObservationPage>(Problem.Storage($"Archy could not read duplicate observations: {exception.Message}")); }
    }
    private static async Task<(ArchitectureTarget Left, ArchitectureTarget Right)?> IdentityAsync(SqliteConnection c,string workspace,string id,CancellationToken ct) { await using var q=c.CreateCommand();q.CommandText="SELECT left_target_kind,left_target_stable_id,right_target_kind,right_target_stable_id FROM duplicate_finding_identities WHERE workspace_id=$workspaceId AND finding_id=$findingId;";q.Parameters.AddWithValue("$workspaceId",workspace);q.Parameters.AddWithValue("$findingId",id);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(r.GetString(0)),r.GetString(1)),new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(r.GetString(2)),r.GetString(3))):null; }
    private static async Task<int> CountAsync(SqliteConnection c,string workspace,string id,CancellationToken ct) { await using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM duplicate_finding_observations WHERE workspace_id=$workspaceId AND finding_id=$findingId;";q.Parameters.AddWithValue("$workspaceId",workspace);q.Parameters.AddWithValue("$findingId",id);return Convert.ToInt32(await q.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture); }
}
