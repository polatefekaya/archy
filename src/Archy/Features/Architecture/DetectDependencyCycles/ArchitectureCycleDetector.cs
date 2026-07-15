using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.DetectDependencyCycles;

/// <summary>Finds one shortest, stable evidence cycle for each hard-edge strongly connected component.</summary>
public sealed class ArchitectureCycleDetector : IArchitectureCycleDetector
{
    public Result<IReadOnlyList<ArchitectureDependencyCycle>> Detect(
        IReadOnlyList<LayerMembershipResolution> membership,
        IReadOnlyList<GraphEdgeFact> eligibleEdges)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(eligibleEdges);
        if (membership.Any(static item => item is null || string.IsNullOrWhiteSpace(item.NodeStableId)) ||
            membership.Select(static item => item.NodeStableId).Distinct(StringComparer.Ordinal).Count() != membership.Count ||
            eligibleEdges.Any(static edge => edge is null || string.IsNullOrWhiteSpace(edge.EdgeId) || string.IsNullOrWhiteSpace(edge.SourceStableId) || string.IsNullOrWhiteSpace(edge.TargetStableId)) ||
            eligibleEdges.Select(static edge => edge.EdgeId).Distinct(StringComparer.Ordinal).Count() != eligibleEdges.Count)
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureDependencyCycle>>(
                Problem.Validation("Cycle detection requires unique layer membership results and uniquely identified graph edges."));
        }

        var assignedNodes = membership
            .Where(static item => item.State == LayerMembershipState.Assigned)
            .Select(static item => item.NodeStableId)
            .OrderBy(static node => node, StringComparer.Ordinal)
            .ToArray();
        var assignedNodeSet = assignedNodes.ToHashSet(StringComparer.Ordinal);
        if (eligibleEdges.Any(edge => !assignedNodeSet.Contains(edge.SourceStableId) || !assignedNodeSet.Contains(edge.TargetStableId)))
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureDependencyCycle>>(
                Problem.Validation("Cycle detection requires every eligible edge to have deterministically assigned source and target layers."));
        }

        var adjacency = BuildAdjacency(assignedNodes, eligibleEdges);
        var reverseAdjacency = BuildReverseAdjacency(assignedNodes, eligibleEdges);
        var components = StronglyConnectedComponents(assignedNodes, adjacency, reverseAdjacency);
        var cycles = components
            .Where(component => component.Count > 1 || HasSelfLoop(component, adjacency))
            .Select(component => FindShortestCycle(component, adjacency) with
            {
                ComponentNodeStableIds = [.. component.OrderBy(static node => node, StringComparer.Ordinal)],
            })
            .OrderBy(static cycle => cycle.EdgeIds.Count)
            .ThenBy(static cycle => string.Join("\u001f", cycle.EdgeIds), StringComparer.Ordinal)
            .ToArray();
        return ResultFactory.Success<IReadOnlyList<ArchitectureDependencyCycle>>(cycles);
    }

    private static Dictionary<string, IReadOnlyList<GraphEdgeFact>> BuildAdjacency(
        IReadOnlyList<string> nodes,
        IReadOnlyList<GraphEdgeFact> edges) =>
        nodes.ToDictionary(
            static node => node,
            node => (IReadOnlyList<GraphEdgeFact>)[.. edges
                .Where(edge => string.Equals(edge.SourceStableId, node, StringComparison.Ordinal))
                .OrderBy(static edge => edge.TargetStableId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            StringComparer.Ordinal);

    private static Dictionary<string, IReadOnlyList<GraphEdgeFact>> BuildReverseAdjacency(
        IReadOnlyList<string> nodes,
        IReadOnlyList<GraphEdgeFact> edges) =>
        nodes.ToDictionary(
            static node => node,
            node => (IReadOnlyList<GraphEdgeFact>)[.. edges
                .Where(edge => string.Equals(edge.TargetStableId, node, StringComparison.Ordinal))
                .OrderBy(static edge => edge.SourceStableId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            StringComparer.Ordinal);

    private static List<IReadOnlySet<string>> StronglyConnectedComponents(
        IReadOnlyList<string> nodes,
        Dictionary<string, IReadOnlyList<GraphEdgeFact>> adjacency,
        Dictionary<string, IReadOnlyList<GraphEdgeFact>> reverseAdjacency)
    {
        var finishingOrder = FinishingOrder(nodes, adjacency, static edge => edge.TargetStableId);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<IReadOnlySet<string>>();
        for (var index = finishingOrder.Count - 1; index >= 0; index--)
        {
            var root = finishingOrder[index];
            if (!assigned.Add(root))
            {
                continue;
            }

            var component = new HashSet<string>(StringComparer.Ordinal) { root };
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                foreach (var edge in reverseAdjacency[node])
                {
                    var predecessor = edge.SourceStableId;
                    if (assigned.Add(predecessor))
                    {
                        component.Add(predecessor);
                        pending.Push(predecessor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static List<string> FinishingOrder(
        IReadOnlyList<string> nodes,
        Dictionary<string, IReadOnlyList<GraphEdgeFact>> adjacency,
        Func<GraphEdgeFact, string> target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var finished = new List<string>();
        foreach (var root in nodes)
        {
            if (!visited.Add(root))
            {
                continue;
            }

            var stack = new Stack<DepthFirstFrame>();
            stack.Push(new DepthFirstFrame(root, 0));
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                var edges = adjacency[frame.Node];
                if (frame.NextEdgeIndex >= edges.Count)
                {
                    finished.Add(frame.Node);
                    continue;
                }

                stack.Push(frame with { NextEdgeIndex = frame.NextEdgeIndex + 1 });
                var next = target(edges[frame.NextEdgeIndex]);
                if (visited.Add(next))
                {
                    stack.Push(new DepthFirstFrame(next, 0));
                }
            }
        }

        return finished;
    }

    private static bool HasSelfLoop(IReadOnlySet<string> component, Dictionary<string, IReadOnlyList<GraphEdgeFact>> adjacency) =>
        component.Count == 1 && adjacency[component.Single()].Any(edge => string.Equals(edge.TargetStableId, edge.SourceStableId, StringComparison.Ordinal));

    private static ArchitectureDependencyCycle FindShortestCycle(
        IReadOnlySet<string> component,
        Dictionary<string, IReadOnlyList<GraphEdgeFact>> adjacency)
    {
        ArchitectureDependencyCycle? winner = null;
        foreach (var start in component.OrderBy(static node => node, StringComparer.Ordinal))
        {
            foreach (var firstEdge in adjacency[start].Where(edge => component.Contains(edge.TargetStableId)))
            {
                var candidate = string.Equals(firstEdge.SourceStableId, firstEdge.TargetStableId, StringComparison.Ordinal)
                    ? new ArchitectureDependencyCycle([start, start], [firstEdge.EdgeId])
                    : BuildCycle(start, firstEdge, component, adjacency);
                if (candidate is not null && IsBetter(candidate, winner))
                {
                    winner = candidate;
                }
            }
        }

        return winner ?? throw new InvalidOperationException("A strongly connected component must contain an explanatory cycle.");
    }

    private static bool IsBetter(ArchitectureDependencyCycle candidate, ArchitectureDependencyCycle? current) =>
        current is null ||
        candidate.EdgeIds.Count < current.EdgeIds.Count ||
        candidate.EdgeIds.Count == current.EdgeIds.Count &&
        string.CompareOrdinal(string.Join("\u001f", candidate.EdgeIds), string.Join("\u001f", current.EdgeIds)) < 0;

    private static ArchitectureDependencyCycle? BuildCycle(
        string start,
        GraphEdgeFact firstEdge,
        IReadOnlySet<string> component,
        Dictionary<string, IReadOnlyList<GraphEdgeFact>> adjacency)
    {
        var previous = new Dictionary<string, GraphEdgeFact>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { firstEdge.TargetStableId };
        var queue = new Queue<string>();
        queue.Enqueue(firstEdge.TargetStableId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in adjacency[current].Where(edge => component.Contains(edge.TargetStableId)))
            {
                if (string.Equals(edge.TargetStableId, start, StringComparison.Ordinal))
                {
                    var path = ReconstructPath(firstEdge.TargetStableId, current, previous);
                    var edges = new List<GraphEdgeFact> { firstEdge };
                    edges.AddRange(path);
                    edges.Add(edge);
                    return new ArchitectureDependencyCycle(
                        [.. edges.Select(static item => item.SourceStableId).Append(start)],
                        [.. edges.Select(static item => item.EdgeId)]);
                }

                if (visited.Add(edge.TargetStableId))
                {
                    previous.Add(edge.TargetStableId, edge);
                    queue.Enqueue(edge.TargetStableId);
                }
            }
        }

        return null;
    }

    private static List<GraphEdgeFact> ReconstructPath(
        string root,
        string current,
        Dictionary<string, GraphEdgeFact> previous)
    {
        var path = new List<GraphEdgeFact>();
        while (!string.Equals(current, root, StringComparison.Ordinal))
        {
            var edge = previous[current];
            path.Add(edge);
            current = edge.SourceStableId;
        }

        path.Reverse();
        return path;
    }

    private sealed record DepthFirstFrame(string Node, int NextEdgeIndex);
}
