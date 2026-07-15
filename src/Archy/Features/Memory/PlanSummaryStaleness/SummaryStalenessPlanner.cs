using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.ComparePublicSurface;

namespace Archy.Features.Memory.PlanSummaryStaleness;

/// <summary>Finds only changed public nodes and their direct graph dependents; it never triggers model work itself.</summary>
public sealed class SummaryStalenessPlanner : ISummaryStalenessPlanner
{
    public SummaryStalenessPlan Plan(
        GraphRevisionSnapshot prior,
        GraphRevisionSnapshot current,
        PublicSurfaceDiff publicSurfaceDiff)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(publicSurfaceDiff);
        if (prior.Revision != publicSurfaceDiff.PriorRevision || current.Revision != publicSurfaceDiff.CurrentRevision)
        {
            throw new ArgumentException("Public-surface diff revisions must exactly match the graph snapshots.", nameof(publicSurfaceDiff));
        }

        var reasons = new Dictionary<string, HashSet<SummaryStalenessReason>>(StringComparer.Ordinal);
        var changedNodeIds = publicSurfaceDiff.Changes
            .SelectMany(static change => new[] { change.PriorNodeStableId, change.CurrentNodeStableId })
            .Where(static stableId => !string.IsNullOrWhiteSpace(stableId))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var nodeId in changedNodeIds)
        {
            AddReason(reasons, nodeId, SummaryStalenessReason.PublicSurfaceChanged);
        }

        foreach (var edge in prior.Edges.Concat(current.Edges)
                     .Where(edge => changedNodeIds.Contains(edge.TargetStableId))
                     .OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal))
        {
            if (!changedNodeIds.Contains(edge.SourceStableId))
            {
                AddReason(reasons, edge.SourceStableId, SummaryStalenessReason.DirectDependentPublicSurfaceChanged);
            }
        }

        return new SummaryStalenessPlan(
            current.Revision,
            [.. reasons
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => new StaleSummaryTarget(entry.Key, [.. entry.Value.OrderBy(static reason => reason)]))]);
    }

    private static void AddReason(
        Dictionary<string, HashSet<SummaryStalenessReason>> reasons,
        string stableId,
        SummaryStalenessReason reason)
    {
        if (!reasons.TryGetValue(stableId, out var targetReasons))
        {
            targetReasons = [];
            reasons.Add(stableId, targetReasons);
        }

        targetReasons.Add(reason);
    }
}
