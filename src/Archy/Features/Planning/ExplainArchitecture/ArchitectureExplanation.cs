using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Planning.ExplainArchitecture;

public sealed record ArchitectureExplanationRequest(string Lookup, int HistoryDepth = 3);
public sealed record ArchitectureFact(string Kind, string Detail, string Provenance);
public sealed record ArchitectureDecisionEvidence(string DecisionId, string Type, string Resolution, string? Note, string Actor, DateTimeOffset OccurredAtUtc, long? GraphRevision);
public sealed record ArchitectureHistoryEvidence(long Revision, string Kind, string Detail, string Provenance);
public sealed record ArchitectureExplanation(long GraphRevision, string ResolvedStableId, IReadOnlyList<ArchitectureFact> Facts, IReadOnlyList<ArchitectureDecisionEvidence> Decisions, IReadOnlyList<ArchitectureFact> AdvisoryContext, IReadOnlyList<ArchitectureHistoryEvidence> History, IReadOnlyList<string> Unknowns, IReadOnlyList<string> SuggestedNextQuestions);

public interface IArchitectureExplainer
{
    ValueTask<Result<ArchitectureExplanation>> ExplainAsync(WorkspaceStateLocation location, ArchitectureExplanationRequest request, CancellationToken cancellationToken);
}

/// <summary>Explains a graph target from persisted facts and explicitly labels non-fact context.</summary>
public sealed class ArchitectureExplainer(IGraphRevisionSnapshotReader snapshots, IArchitectureDecisionRepository decisions) : IArchitectureExplainer
{
    public async ValueTask<Result<ArchitectureExplanation>> ExplainAsync(WorkspaceStateLocation location, ArchitectureExplanationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Lookup) || request.Lookup.Length > 320 || request.HistoryDepth is < 0 or > 20)
            return ResultFactory.Failure<ArchitectureExplanation>(Problem.Validation("Architecture explanation lookup is required (up to 320 characters) and history depth must be between 0 and 20."));
        var snapshot = await snapshots.ReadActiveAsync(location, cancellationToken);
        if (!snapshot.IsSuccess) return ResultFactory.Failure<ArchitectureExplanation>(snapshot.Problem!);
        if (snapshot.Value is null) return ResultFactory.Failure<ArchitectureExplanation>(Problem.NotFound("No active graph revision is available to explain."));
        var resolved = Resolve(snapshot.Value, request.Lookup);
        if (resolved is null) return ResultFactory.Failure<ArchitectureExplanation>(Problem.NotFound($"No active graph node matches '{request.Lookup}'."));
        var facts = BuildFacts(snapshot.Value, resolved);
        var listed = await decisions.ListForTargetAsync(location, new ArchitectureTarget(ArchitectureTargetKind.GraphNode, resolved.StableId), cancellationToken);
        if (!listed.IsSuccess) return ResultFactory.Failure<ArchitectureExplanation>(listed.Problem!);
        var history = request.HistoryDepth == 0 ? [] : await ReadHistoryAsync(location, resolved, request.HistoryDepth, cancellationToken);
        var unknowns = new List<string>();
        if (history.Count == 0) unknowns.Add("No bounded historical revision evidence is available for this node.");
        if (listed.Value!.Count == 0) unknowns.Add("No architecture decisions target this graph node.");
        return ResultFactory.Success(new ArchitectureExplanation(snapshot.Value.Revision, resolved.StableId, facts, [.. listed.Value.Select(ToEvidence)], [], history, unknowns, ["get_dependents", "get_module_rules", "impact_analysis"]));
    }

    private static GraphNodeFact? Resolve(GraphRevisionSnapshot snapshot, string lookup) => snapshot.Nodes.FirstOrDefault(node => string.Equals(node.StableId, lookup, StringComparison.Ordinal))
        ?? snapshot.Nodes.FirstOrDefault(node => string.Equals(node.FilePath, lookup, StringComparison.Ordinal))
        ?? snapshot.Nodes.Where(node => node.DisplayName.Contains(lookup, StringComparison.OrdinalIgnoreCase) || node.CanonicalKey.Contains(lookup, StringComparison.OrdinalIgnoreCase)).OrderBy(node => node.StableId, StringComparer.Ordinal).FirstOrDefault();

    private static List<ArchitectureFact> BuildFacts(GraphRevisionSnapshot snapshot, GraphNodeFact node)
    {
        var facts = new List<ArchitectureFact>
        {
            new("declaration", $"{node.NodeKind} '{node.DisplayName}' ({node.CanonicalKey}).", "graph_nodes"),
            new("source", node.FilePath is null ? "No persisted source path is available." : $"Defined at {node.FilePath}:{node.StartLine?.ToString() ?? "?"}.", "graph_nodes"),
            new("provider", $"Provider '{node.Provider}' reported confidence {node.Confidence:R}.", "graph_nodes")
        };
        foreach (var edge in snapshot.Edges.Where(edge => edge.SourceStableId == node.StableId || edge.TargetStableId == node.StableId).OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).Take(40))
            facts.Add(new("dependency", edge.SourceStableId == node.StableId ? $"Depends on {edge.TargetStableId} through {edge.EdgeKind}." : $"Is depended on by {edge.SourceStableId} through {edge.EdgeKind}.", "graph_edges"));
        foreach (var symbol in snapshot.Symbols.Where(symbol => symbol.NodeStableId == node.StableId).OrderBy(symbol => symbol.SymbolId, StringComparer.Ordinal).Take(20))
            facts.Add(new("public_surface", $"{symbol.Visibility} symbol {symbol.FullyQualifiedName} ({symbol.NormalizedSignature}).", "graph_symbols"));
        return facts;
    }

    private static ArchitectureDecisionEvidence ToEvidence(ArchitectureDecision decision) => new(decision.DecisionId, decision.DecisionType, decision.Resolution.ToString(), decision.Note, $"{decision.ActorKind}:{decision.ActorId}", decision.OccurredAtUtc, decision.GraphRevision);

    private static async Task<IReadOnlyList<ArchitectureHistoryEvidence>> ReadHistoryAsync(WorkspaceStateLocation location, GraphNodeFact node, int depth, CancellationToken cancellationToken)
    {
        // History is intentionally advisory: it reads immutable version rows and does not infer a rename or decision.
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = location.DatabasePath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT valid_from_revision, valid_to_revision, file_path FROM graph_node_versions WHERE workspace_id=$workspace AND stable_id=$stableId ORDER BY valid_from_revision DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$workspace", location.WorkspaceId); command.Parameters.AddWithValue("$stableId", node.StableId); command.Parameters.AddWithValue("$limit", depth);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<ArchitectureHistoryEvidence>();
            while (await reader.ReadAsync(cancellationToken)) { var revision = reader.GetInt64(0); long? until = reader.IsDBNull(1) ? null : reader.GetInt64(1); var path = reader.IsDBNull(2) ? null : reader.GetString(2); result.Add(new(revision, until is null ? "active" : "historical", until is null ? $"Present from revision {revision}." : $"Present from revision {revision} through {until}.", path ?? "graph_node_versions")); }
            return result;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { return []; }
    }
}
