using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Integrations.Codex.ComposeChangeSummary;

public sealed record ArchitectureDelta(string StableId, string Kind, string? FilePath, string Detail);
public sealed record ArchitectureChangeSummary(long BaseRevision, long CurrentRevision, IReadOnlyList<ArchitectureDelta> AddedNodes, IReadOnlyList<ArchitectureDelta> RemovedNodes, IReadOnlyList<ArchitectureDelta> ChangedNodes, int AddedEdges, int RemovedEdges, string Markdown, bool Abstained, string? AbstentionReason);
public interface IArchitectureChangeSummaryComposer { ValueTask<Result<ArchitectureChangeSummary>> ComposeAsync(WorkspaceStateLocation location, long baseRevision, CancellationToken cancellationToken); }

/// <summary>Reads two immutable graph revisions and produces bounded deterministic delta evidence; it never invokes Git or a remote provider.</summary>
public sealed class ArchitectureChangeSummaryComposer(IWorkspaceLockManager lockManager) : IArchitectureChangeSummaryComposer
{
    public async ValueTask<Result<ArchitectureChangeSummary>> ComposeAsync(WorkspaceStateLocation location, long baseRevision, CancellationToken cancellationToken)
    {
        if (baseRevision < 1) return ResultFactory.Failure<ArchitectureChangeSummary>(Problem.Validation("A positive base graph revision is required; Archy will not assume Git HEAD equals a graph revision."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<ArchitectureChangeSummary>(lease.Problem!); using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = location.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString()); await connection.OpenAsync(cancellationToken);
            var current = await ActiveRevisionAsync(connection, location.WorkspaceId, cancellationToken);
            if (current is null) return ResultFactory.Success<ArchitectureChangeSummary>(Abstain(baseRevision, "No active graph revision is available."));
            if (!await RevisionExistsAsync(connection, location.WorkspaceId, baseRevision, cancellationToken)) return ResultFactory.Success<ArchitectureChangeSummary>(Abstain(baseRevision, $"Base graph revision {baseRevision} is unavailable or was compacted."));
            var before = await NodesAsync(connection, baseRevision, cancellationToken); var after = await NodesAsync(connection, current.Value, cancellationToken);
            var added = after.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(500).Select(id => Delta(id, after[id], "added")).ToArray();
            var removed = before.Keys.Except(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(500).Select(id => Delta(id, before[id], "removed")).ToArray();
            var changed = after.Keys.Intersect(before.Keys, StringComparer.Ordinal).Where(id => !Equals(before[id], after[id])).Order(StringComparer.Ordinal).Take(500).Select(id => Delta(id, after[id], "changed")).ToArray();
            var beforeEdges = await EdgeCountAsync(connection, baseRevision, cancellationToken); var afterEdges = await EdgeCountAsync(connection, current.Value, cancellationToken);
            var markdown = $"## Architecture change summary\n\nGraph revision `{baseRevision}` → `{current}`.\n\n- Added nodes: {added.Length}\n- Removed nodes: {removed.Length}\n- Changed nodes: {changed.Length}\n- Edge count: {beforeEdges} → {afterEdges}\n\nThis is deterministic graph evidence, not a GitHub PR update.";
            return ResultFactory.Success<ArchitectureChangeSummary>(new(baseRevision, current.Value, added, removed, changed, Math.Max(0, afterEdges - beforeEdges), Math.Max(0, beforeEdges - afterEdges), markdown, false, null));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<ArchitectureChangeSummary>(Problem.Storage($"Archy could not read graph delta evidence: {exception.Message}")); }
    }
    private static ArchitectureChangeSummary Abstain(long baseRevision, string reason) => new(baseRevision, 0, [], [], [], 0, 0, "## Architecture change summary\n\nNo trustworthy graph delta is available.", true, reason);
    private static async Task<long?> ActiveRevisionAsync(SqliteConnection connection, string workspace, CancellationToken ct) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id=$workspace;"; command.Parameters.AddWithValue("$workspace", workspace); var value = await command.ExecuteScalarAsync(ct); return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
    private static async Task<bool> RevisionExistsAsync(SqliteConnection connection, string workspace, long revision, CancellationToken ct) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE workspace_id=$workspace AND revision=$revision);"; command.Parameters.AddWithValue("$workspace", workspace); command.Parameters.AddWithValue("$revision", revision); return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) != 0; }
    private static async Task<Dictionary<string, Node>> NodesAsync(SqliteConnection connection, long revision, CancellationToken ct) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT stable_id,node_kind,file_path,content_hash FROM graph_nodes WHERE revision=$revision ORDER BY stable_id;"; command.Parameters.AddWithValue("$revision", revision); await using var reader = await command.ExecuteReaderAsync(ct); var result = new Dictionary<string, Node>(StringComparer.Ordinal); while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0), new(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3))); return result; }
    private static async Task<int> EdgeCountAsync(SqliteConnection connection, long revision, CancellationToken ct) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM graph_edges WHERE revision=$revision;"; command.Parameters.AddWithValue("$revision", revision); return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture); }
    private static ArchitectureDelta Delta(string stableId, Node node, string kind) => new(stableId, kind, node.FilePath, $"{node.Kind} at {node.FilePath ?? "unknown source path"}.");
    private sealed record Node(string Kind, string? FilePath, string ContentHash);
}
