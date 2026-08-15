using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Planning.AnalyzeImpact;

public sealed class ImpactAnalyzer(IGraphTraversalReader traversalReader, IGraphRevisionSnapshotReader snapshotReader) : IImpactAnalyzer
{
    public async ValueTask<Result<ImpactAnalysisResult>> AnalyzeAsync(WorkspaceStateLocation location, ImpactAnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetStableId) || !Enum.IsDefined(request.Direction) || request.Depth is < 1 or > 8 || request.MaxNodes is < 1 or > 500) return ResultFactory.Failure<ImpactAnalysisResult>(Problem.Validation("Impact analysis requires a target, direction, depth from 1 through 8, and max nodes from 1 through 500."));
        var snapshot = await snapshotReader.ReadActiveAsync(location, cancellationToken); if (!snapshot.IsSuccess) return ResultFactory.Failure<ImpactAnalysisResult>(snapshot.Problem!); if (snapshot.Value is null) return ResultFactory.Success(new ImpactAnalysisResult(0, request.TargetStableId, [], [], [], [new("incomplete_analysis", "No active graph revision exists.", true)], false, true, "No active graph revision exists."));
        if (!snapshot.Value.Nodes.Any(node => node.StableId == request.TargetStableId)) return ResultFactory.Success(new ImpactAnalysisResult(snapshot.Value.Revision, request.TargetStableId, [], [], [], [], false, true, "The target does not exist in the active graph revision."));
        var directions = request.Direction switch { ImpactDirection.Dependencies => new[] { GraphTraversalDirection.Dependencies }, ImpactDirection.Dependents => new[] { GraphTraversalDirection.Dependents }, _ => new[] { GraphTraversalDirection.Dependents, GraphTraversalDirection.Dependencies } };
        var paths = new List<ImpactPath>(); var truncated = false;
        foreach (var direction in directions)
        {
            var traversed = await traversalReader.TraverseAsync(location, new(request.TargetStableId, direction, snapshot.Value.Revision, request.Depth, request.MaxNodes), cancellationToken); if (!traversed.IsSuccess) return ResultFactory.Failure<ImpactAnalysisResult>(traversed.Problem!);
            truncated |= traversed.Value!.IsTruncated;
            foreach (var edge in traversed.Value.Edges.Where(edge => request.EdgeKinds is null or { Count: 0 } || request.EdgeKinds.Contains(edge.EdgeKind, StringComparer.Ordinal)))
            {
                var reached = direction == GraphTraversalDirection.Dependencies ? edge.TargetStableId : edge.SourceStableId;
                paths.Add(new(reached, edge.Depth, edge.EdgeId, edge.EdgeKind, direction));
            }
        }
        var unique = paths.GroupBy(path => $"{path.Direction}\u001f{path.StableId}", StringComparer.Ordinal).Select(group => group.OrderBy(path => path.Depth).ThenBy(path => path.ViaEdgeId, StringComparer.Ordinal).First()).OrderBy(path => path.Depth).ThenBy(path => path.StableId, StringComparer.Ordinal).Take(request.MaxNodes).ToArray();
        truncated |= paths.Select(path => path.StableId).Distinct(StringComparer.Ordinal).Count() > unique.Length;
        var direct = unique.Where(path => path.Depth == 1).ToArray(); var transitive = unique.Where(path => path.Depth > 1).ToArray();
        var publicSurface = unique.Where(path => snapshot.Value.Symbols.Any(symbol => symbol.NodeStableId == path.StableId && string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase))).Select(path => path.StableId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var risks = new List<ImpactRisk>(); if (publicSurface.Length > 0) risks.Add(new("public_api", $"{publicSurface.Length} reachable public symbol(s) may be affected.", true)); if (unique.Length >= 50) risks.Add(new("high_blast_radius", $"{unique.Length} reachable graph node(s) are within the requested bound.", true)); if (unique.Any(path => path.EdgeKind.StartsWith("configuration_", StringComparison.Ordinal))) risks.Add(new("configuration_contract", "Reachable configuration definition or read facts may be affected.", true)); if (unique.Any(path => path.EdgeKind.StartsWith("message_", StringComparison.Ordinal))) risks.Add(new("message_contract", "Reachable message publication, consumption, or topology facts may be affected.", true)); if (unique.Any(path => path.EdgeKind.StartsWith("di_", StringComparison.Ordinal))) risks.Add(new("service_registration", "Reachable dependency-injection registration or consumption facts may be affected.", true)); if (truncated) risks.Add(new("incomplete_analysis", "Traversal was truncated at the requested node bound.", true));
        return ResultFactory.Success(new ImpactAnalysisResult(snapshot.Value.Revision, request.TargetStableId, direct, transitive, publicSurface, risks, truncated, false, null));
    }
}
