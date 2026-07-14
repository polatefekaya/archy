using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

public sealed class GraphRevisionStore(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IGraphRevisionStore
{
    public async ValueTask<Result<CommittedGraphRevision>> CommitAsync(WorkspaceStateLocation location, string runId, IReadOnlyList<GraphNodeFact> nodes, IReadOnlyList<GraphEdgeFact> edges, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentException.ThrowIfNullOrWhiteSpace(runId); ArgumentNullException.ThrowIfNull(nodes); ArgumentNullException.ThrowIfNull(edges);
        var invalid = Validate(nodes, edges); if (invalid is not null) return ResultFactory.Failure<CommittedGraphRevision>(invalid);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<CommittedGraphRevision>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var c = new SqliteConnection($"Data Source={location.DatabasePath}"); await c.OpenAsync(cancellationToken);
            await using var t = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
            var workspaceId = await ScalarStringAsync(c, t, "SELECT workspace_id FROM analysis_runs WHERE run_id=$id AND status='running';", cancellationToken, ("$id", runId));
            if (workspaceId is null || !string.Equals(workspaceId, location.WorkspaceId, StringComparison.Ordinal)) return ResultFactory.Failure<CommittedGraphRevision>(Problem.Conflict($"Analysis run '{runId}' is not running for this workspace."));
            var committedAt = timeProvider.GetUtcNow();
            await ExecuteAsync(c, t, "INSERT INTO graph_revisions(workspace_id, run_id, committed_at_utc) VALUES ($workspace,$run,$at);", cancellationToken, ("$workspace",location.WorkspaceId),("$run",runId),("$at",committedAt.ToString("O")));
            var revision = await ScalarLongAsync(c, t, "SELECT last_insert_rowid();", cancellationToken);
            foreach (var node in nodes) await ExecuteAsync(c,t,"INSERT INTO graph_nodes(revision,stable_id,node_kind,canonical_key,display_name,file_path,start_line,end_line,provider,confidence,evidence_json) VALUES ($r,$id,$kind,$key,$name,$file,$start,$end,$provider,$confidence,$evidence);",cancellationToken,("$r",revision),("$id",node.StableId),("$kind",node.NodeKind),("$key",node.CanonicalKey),("$name",node.DisplayName),("$file",node.FilePath),("$start",node.StartLine),("$end",node.EndLine),("$provider",node.Provider),("$confidence",node.Confidence),("$evidence",node.EvidenceJson));
            foreach (var edge in edges) await ExecuteAsync(c,t,"INSERT INTO graph_edges(revision,edge_id,source_stable_id,target_stable_id,edge_kind,normalized_join_key,provider,confidence,evidence_json) VALUES ($r,$id,$source,$target,$kind,$join,$provider,$confidence,$evidence);",cancellationToken,("$r",revision),("$id",edge.EdgeId),("$source",edge.SourceStableId),("$target",edge.TargetStableId),("$kind",edge.EdgeKind),("$join",edge.NormalizedJoinKey),("$provider",edge.Provider),("$confidence",edge.Confidence),("$evidence",edge.EvidenceJson));
            await ExecuteAsync(c,t,"UPDATE analysis_runs SET status='succeeded',completed_at_utc=$at,graph_revision=$revision WHERE run_id=$run AND status='running';",cancellationToken,("$at",committedAt.ToString("O")),("$revision",revision),("$run",runId));
            await ExecuteAsync(c,t,"INSERT INTO analysis_run_events(run_id,event_type,occurred_at_utc,graph_revision) VALUES ($run,'succeeded',$at,$revision);",cancellationToken,("$run",runId),("$at",committedAt.ToString("O")),("$revision",revision));
            await t.CommitAsync(cancellationToken); return ResultFactory.Success(new CommittedGraphRevision(revision,runId,committedAt,nodes.Count,edges.Count));
        }
        catch (SqliteException e) { return ResultFactory.Failure<CommittedGraphRevision>(Problem.Storage($"Archy could not commit graph revision: {e.Message}")); }
    }

    private static Problem? Validate(IReadOnlyList<GraphNodeFact> nodes, IReadOnlyList<GraphEdgeFact> edges) { var ids=new HashSet<string>(StringComparer.Ordinal); foreach(var n in nodes) { if(string.IsNullOrWhiteSpace(n.StableId)||string.IsNullOrWhiteSpace(n.NodeKind)||string.IsNullOrWhiteSpace(n.CanonicalKey)||string.IsNullOrWhiteSpace(n.Provider)||string.IsNullOrWhiteSpace(n.EvidenceJson)||n.Confidence is < 0 or > 1||!ids.Add(n.StableId)) return Problem.Validation("Graph nodes require unique stable IDs, evidence, provider, and confidence between zero and one."); } var edgeIds=new HashSet<string>(StringComparer.Ordinal); foreach(var e in edges) if(string.IsNullOrWhiteSpace(e.EdgeId)||!edgeIds.Add(e.EdgeId)||!ids.Contains(e.SourceStableId)||!ids.Contains(e.TargetStableId)||string.IsNullOrWhiteSpace(e.EdgeKind)||string.IsNullOrWhiteSpace(e.Provider)||string.IsNullOrWhiteSpace(e.EvidenceJson)||e.Confidence is < 0 or > 1) return Problem.Validation("Graph edges require unique IDs, in-revision endpoints, evidence, provider, and confidence between zero and one."); return null; }
    private static async Task ExecuteAsync(SqliteConnection c, SqliteTransaction t,string sql,CancellationToken ct,params (string Name,object? Value)[] p){await using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;foreach(var x in p)cmd.Parameters.AddWithValue(x.Name,x.Value??DBNull.Value);_ = await cmd.ExecuteNonQueryAsync(ct);}
    private static async Task<long> ScalarLongAsync(SqliteConnection c,SqliteTransaction t,string sql,CancellationToken ct){await using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture);}
    private static async Task<string?> ScalarStringAsync(SqliteConnection c,SqliteTransaction t,string sql,CancellationToken ct,params (string Name,object? Value)[] p){await using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;foreach(var x in p)cmd.Parameters.AddWithValue(x.Name,x.Value??DBNull.Value);var value=await cmd.ExecuteScalarAsync(ct);return value is null or DBNull?null:Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);}
}
