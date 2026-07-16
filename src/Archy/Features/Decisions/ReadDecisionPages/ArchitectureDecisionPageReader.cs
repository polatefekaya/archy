using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Decisions.ReadDecisionPages;

/// <summary>Pages decision identities first, then reads only their complete target sets.</summary>
public sealed class ArchitectureDecisionPageReader(IWorkspaceLockManager lockManager) : IArchitectureDecisionPageReader
{
    public async ValueTask<Result<ArchitectureDecisionPage>> ReadAsync(WorkspaceStateLocation location, ArchitectureTarget target, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(target.Kind) || string.IsNullOrWhiteSpace(target.StableId) || offset is < 0 or > 1_000_000 || limit is < 1 or > 100)
            return ResultFactory.Failure<ArchitectureDecisionPage>(Problem.Validation("Decision pages require a valid target, offset between 0 and 1000000, and limit between 1 and 100."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<ArchitectureDecisionPage>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var kind = ArchitectureTargetCodec.ToStorageValue(target.Kind);
            var total = await CountAsync(connection, location.WorkspaceId, kind, target.StableId, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "WITH selected AS (SELECT d.decision_id, d.decision_type, d.resolution, d.note, d.actor_kind, d.actor_id, d.session_id, d.graph_revision, d.occurred_at_utc FROM decisions d INNER JOIN decision_targets matched ON matched.decision_id = d.decision_id WHERE d.workspace_id = $workspaceId AND matched.target_kind = $targetKind AND matched.target_stable_id = $targetStableId ORDER BY d.occurred_at_utc DESC, d.decision_id DESC LIMIT $limit OFFSET $offset) SELECT s.decision_id, s.decision_type, s.resolution, s.note, s.actor_kind, s.actor_id, s.session_id, s.graph_revision, s.occurred_at_utc, t.target_kind, t.target_stable_id FROM selected s INNER JOIN decision_targets t ON t.decision_id = s.decision_id ORDER BY s.occurred_at_utc DESC, s.decision_id DESC, t.target_ordinal;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$targetKind", kind);
            command.Parameters.AddWithValue("$targetStableId", target.StableId);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var builders = new Dictionary<string, Builder>(StringComparer.Ordinal);
            var order = new List<Builder>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                if (!builders.TryGetValue(id, out var builder))
                {
                    builder = new Builder(id, reader.GetString(1), ParseResolution(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt64(7), DateTimeOffset.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture));
                    builders.Add(id, builder); order.Add(builder);
                }
                builder.Targets.Add(new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(9)), reader.GetString(10)));
            }
            return ResultFactory.Success(new ArchitectureDecisionPage(target, offset, limit, total, [.. order.Select(static item => item.ToDecision())]));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<ArchitectureDecisionPage>(Problem.Storage($"Archy could not read decision history: {exception.Message}")); }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string workspaceId, string kind, string stableId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM decisions d INNER JOIN decision_targets t ON t.decision_id = d.decision_id WHERE d.workspace_id = $workspaceId AND t.target_kind = $kind AND t.target_stable_id = $stableId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId); command.Parameters.AddWithValue("$kind", kind); command.Parameters.AddWithValue("$stableId", stableId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DecisionResolution ParseResolution(string value) => Enum.TryParse<DecisionResolution>(value, true, out var resolution) ? resolution : throw new InvalidOperationException($"Unknown persisted decision resolution '{value}'.");
    private sealed class Builder(string id, string type, DecisionResolution resolution, string? note, string actorKind, string actorId, string? sessionId, long? graphRevision, DateTimeOffset occurred) { public List<ArchitectureTarget> Targets { get; } = []; public ArchitectureDecision ToDecision() => new(id, type, resolution, note, actorKind, actorId, sessionId, graphRevision, [.. Targets], occurred); }
}
