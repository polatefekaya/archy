using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Similarity.BuildSimilarityClusters;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.ComposeSessionArchitectureContext;

public sealed record SessionArchitectureContext(long GraphRevision, IReadOnlyList<string> FocusedPaths, IReadOnlyList<string> PublicSurfaceStableIds, IReadOnlyList<string> DecisionIds, IReadOnlyList<string> SimilarityClusterIds, IReadOnlyList<string> Abstentions, string Text);
public interface ISessionArchitectureContextComposer { ValueTask<Result<SessionArchitectureContext>> ComposeAsync(WorkspaceStateLocation location, CancellationToken cancellationToken); }

/// <summary>Creates a capped local SessionStart context from persisted facts only; it does not inspect prompts or call a model.</summary>
public sealed class SessionArchitectureContextComposer(IGraphRevisionSnapshotReader snapshots, IArchitectureDecisionRepository? decisionRepository = null, ISimilarityClusterRepository? similarityClusters = null) : ISessionArchitectureContextComposer
{
    public async ValueTask<Result<SessionArchitectureContext>> ComposeAsync(WorkspaceStateLocation location, CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.ReadActiveAsync(location, cancellationToken); if (!snapshot.IsSuccess) return ResultFactory.Failure<SessionArchitectureContext>(snapshot.Problem!);
        if (snapshot.Value is null) return ResultFactory.Success(new SessionArchitectureContext(0, [], [], [], [], ["No active graph revision is available."], "No active graph context is available; run `archy analyze` when architecture guidance is needed."));
        var paths = snapshot.Value.Nodes.Select(node => node.FilePath).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8).ToArray();
        var publicSurface = snapshot.Value.Symbols.Where(symbol => string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase)).Select(symbol => symbol.NodeStableId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8).ToArray();
        var decisions = await ReadDecisionIdsAsync(location, publicSurface, cancellationToken);
        var clusters = await ReadCurrentClusterIdsAsync(location, snapshot.Value.Revision, publicSurface, cancellationToken);
        var abstentions = decisions.Abstentions.Concat(clusters.Abstentions).ToArray();
        var text = $"Persisted graph context: revision {snapshot.Value.Revision}; {snapshot.Value.Nodes.Count} nodes; {snapshot.Value.Edges.Count} edges; representative paths: {(paths.Length == 0 ? "none" : string.Join(", ", paths))}; public surface samples: {(publicSurface.Length == 0 ? "none" : string.Join(", ", publicSurface))}; applicable decision references: {(decisions.DecisionIds.Count == 0 ? "none" : string.Join(", ", decisions.DecisionIds))}; current similarity clusters: {(clusters.ClusterIds.Count == 0 ? "none" : string.Join(", ", clusters.ClusterIds))}.";
        return ResultFactory.Success(new SessionArchitectureContext(snapshot.Value.Revision, paths, publicSurface, decisions.DecisionIds, clusters.ClusterIds, abstentions, text.Length <= 2000 ? text : text[..2000]));
    }

    private async ValueTask<(IReadOnlyList<string> ClusterIds, IReadOnlyList<string> Abstentions)> ReadCurrentClusterIdsAsync(WorkspaceStateLocation location, long graphRevision, string[] publicSurface, CancellationToken cancellationToken)
    {
        if (publicSurface.Length == 0) return ([], []);
        var repository = similarityClusters ?? new SimilarityClusterRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var revision = await repository.ReadLatestAsync(location, graphRevision, cancellationToken);
        if (!revision.IsSuccess) return ([], ["Current similarity cluster evidence is unavailable for this session context."]);
        if (revision.Value is null) return ([], []);
        var publicIds = publicSurface.ToHashSet(StringComparer.Ordinal);
        return ([.. revision.Value.Clusters.Where(cluster => cluster.Members.Any(member => publicIds.Contains(member.StableId))).Select(cluster => cluster.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8)], []);
    }

    private async ValueTask<(IReadOnlyList<string> DecisionIds, IReadOnlyList<string> Abstentions)> ReadDecisionIdsAsync(WorkspaceStateLocation location, string[] publicSurface, CancellationToken cancellationToken)
    {
        if (publicSurface.Length == 0) return ([], []);
        var repository = decisionRepository ?? new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var decisions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var stableId in publicSurface.Take(8))
        {
            var read = await repository.ListForTargetAsync(location, new(ArchitectureTargetKind.GraphNode, stableId), cancellationToken);
            if (!read.IsSuccess) return ([.. decisions.Take(20)], ["Architecture decision references are unavailable for this session context."]);
            foreach (var decision in read.Value!.Take(20)) decisions.Add(decision.DecisionId);
        }
        return ([.. decisions.Take(20)], []);
    }
}
