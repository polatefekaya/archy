using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ReconcileDuplicateFindingLifecycle;

/// <summary>Append-only revision lifecycle; history is retained while current surfaces query only active findings.</summary>
public sealed class DuplicateFindingLifecycleRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IDuplicateFindingLifecycleRepository
{
    public async ValueTask<Result<IReadOnlyList<DuplicateFindingLifecycle>>> ReconcileAsync(WorkspaceStateLocation location, long graphRevision, IReadOnlyCollection<string> activeFindingIds, IReadOnlyDictionary<string, string> supersededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(activeFindingIds); ArgumentNullException.ThrowIfNull(supersededBy);
        if (graphRevision < 1 || activeFindingIds.Any(string.IsNullOrWhiteSpace) || supersededBy.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) || activeFindingIds.Contains(pair.Key))) return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(Problem.Validation("Duplicate lifecycle reconciliation requires valid, non-conflicting finding identities."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(lease.Problem!); using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var known = await ReadKnownAsync(connection, transaction, location.WorkspaceId, cancellationToken);
            var requested = activeFindingIds.Concat(supersededBy.Keys).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            if (!requested.IsSubsetOf(known)) return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(Problem.Conflict("Duplicate lifecycle reconciliation referenced a finding outside this workspace."));
            var now = timeProvider.GetUtcNow(); var events = new List<DuplicateFindingLifecycle>();
            foreach (var findingId in known.OrderBy(static id => id, StringComparer.Ordinal))
            {
                var current = await ReadLatestAsync(connection, transaction, location.WorkspaceId, findingId, graphRevision, cancellationToken);
                var desired = supersededBy.TryGetValue(findingId, out var successor) ? (DuplicateFindingLifecycleState.Superseded, successor, "candidate-superseded") : activeFindingIds.Contains(findingId) ? (DuplicateFindingLifecycleState.Active, (string?)null, "candidate-present") : (DuplicateFindingLifecycleState.Closed, (string?)null, "candidate-absent");
                if (current is not null && current.State == desired.Item1 && string.Equals(current.SupersededByFindingId, desired.Item2, StringComparison.Ordinal)) continue;
                await InsertAsync(connection, transaction, location.WorkspaceId, findingId, graphRevision, desired.Item1, desired.Item2, desired.Item3, now, cancellationToken); events.Add(new DuplicateFindingLifecycle(findingId, graphRevision, desired.Item1, desired.Item2, desired.Item3, now));
            }
            await transaction.CommitAsync(cancellationToken); return ResultFactory.Success<IReadOnlyList<DuplicateFindingLifecycle>>([.. events]);
        }
        catch (SqliteException exception) { return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(Problem.Storage($"Archy could not reconcile duplicate lifecycle state: {exception.Message}")); }
    }
    public async ValueTask<Result<IReadOnlyList<DuplicateFindingLifecycle>>> ListActiveAtAsync(WorkspaceStateLocation location, long graphRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); if (graphRevision < 1) return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(Problem.Validation("A duplicate lifecycle query requires a graph revision."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(lease.Problem!); using var held = lease.Value;
        try { SQLitePCL.Batteries_V2.Init(); await using var c = new SqliteConnection($"Data Source={location.DatabasePath}"); await c.OpenAsync(cancellationToken); var known = await ReadKnownAsync(c, null, location.WorkspaceId, cancellationToken); var output = new List<DuplicateFindingLifecycle>(); foreach (var id in known) { var state = await ReadLatestAsync(c, null, location.WorkspaceId, id, graphRevision, cancellationToken); if (state?.State == DuplicateFindingLifecycleState.Active) output.Add(state); } return ResultFactory.Success<IReadOnlyList<DuplicateFindingLifecycle>>([.. output.OrderBy(static item => item.FindingId, StringComparer.Ordinal)]); }
        catch (SqliteException exception) { return ResultFactory.Failure<IReadOnlyList<DuplicateFindingLifecycle>>(Problem.Storage($"Archy could not read duplicate lifecycle state: {exception.Message}")); }
    }
    private static async Task<HashSet<string>> ReadKnownAsync(SqliteConnection c, SqliteTransaction? t, string workspace, CancellationToken ct) { await using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "SELECT finding_id FROM duplicate_finding_identities WHERE workspace_id=$workspaceId;"; q.Parameters.AddWithValue("$workspaceId", workspace); await using var r = await q.ExecuteReaderAsync(ct); var ids = new HashSet<string>(StringComparer.Ordinal); while (await r.ReadAsync(ct)) ids.Add(r.GetString(0)); return ids; }
    private static async Task<DuplicateFindingLifecycle?> ReadLatestAsync(SqliteConnection c, SqliteTransaction? t, string workspace, string id, long revision, CancellationToken ct) { await using var q = c.CreateCommand(); q.Transaction=t; q.CommandText="SELECT graph_revision,state,superseded_by_finding_id,reason,occurred_at_utc FROM duplicate_finding_lifecycle_events WHERE workspace_id=$workspaceId AND finding_id=$findingId AND graph_revision <= $graphRevision ORDER BY graph_revision DESC LIMIT 1;"; q.Parameters.AddWithValue("$workspaceId",workspace);q.Parameters.AddWithValue("$findingId",id);q.Parameters.AddWithValue("$graphRevision",revision);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;return new DuplicateFindingLifecycle(id,r.GetInt64(0),r.GetString(1) switch {"active"=>DuplicateFindingLifecycleState.Active,"closed"=>DuplicateFindingLifecycleState.Closed,"superseded"=>DuplicateFindingLifecycleState.Superseded,_=>throw new InvalidOperationException("Unknown lifecycle state.")},r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),DateTimeOffset.Parse(r.GetString(4),System.Globalization.CultureInfo.InvariantCulture)); }
    private static async Task InsertAsync(SqliteConnection c,SqliteTransaction t,string workspace,string id,long revision,DuplicateFindingLifecycleState state,string? successor,string reason,DateTimeOffset now,CancellationToken ct){await using var q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO duplicate_finding_lifecycle_events(lifecycle_event_id,workspace_id,finding_id,graph_revision,state,superseded_by_finding_id,reason,occurred_at_utc) VALUES($eventId,$workspaceId,$findingId,$graphRevision,$state,$successor,$reason,$now);";q.Parameters.AddWithValue("$eventId",Guid.NewGuid().ToString("N"));q.Parameters.AddWithValue("$workspaceId",workspace);q.Parameters.AddWithValue("$findingId",id);q.Parameters.AddWithValue("$graphRevision",revision);q.Parameters.AddWithValue("$state",state.ToString().ToLowerInvariant());q.Parameters.AddWithValue("$successor",(object?)successor??DBNull.Value);q.Parameters.AddWithValue("$reason",reason);q.Parameters.AddWithValue("$now",now.ToString("O",System.Globalization.CultureInfo.InvariantCulture));await q.ExecuteNonQueryAsync(ct);}
}
