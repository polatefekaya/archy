using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

/// <summary>Evaluates configured layer direction against only hard-policy-eligible graph edges.</summary>
public sealed class LayerDependencyRuleEvaluator(
    ILayerMembershipResolver membershipResolver,
    IHardArchitectureEdgePolicy edgePolicy,
    IArchitectureCycleDetector cycleDetector) : ILayerDependencyRuleEvaluator
{
    public Result<LayerDependencyEvaluation> Evaluate(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<LayerRuleConfiguration> layers,
        ArchitectureEnforcementConfiguration enforcement)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(enforcement);
        var enforcementProblem = ArchitectureEnforcementConfigurationValidator.Validate(enforcement);
        if (enforcementProblem is not null)
        {
            return ResultFactory.Failure<LayerDependencyEvaluation>(enforcementProblem);
        }

        if (edges.Any(static edge => edge is null || string.IsNullOrWhiteSpace(edge.EdgeId) || string.IsNullOrWhiteSpace(edge.SourceStableId) || string.IsNullOrWhiteSpace(edge.TargetStableId) || string.IsNullOrWhiteSpace(edge.EdgeKind) || !double.IsFinite(edge.Confidence) || edge.Confidence is < 0 or > 1) ||
            edges.Select(static edge => edge.EdgeId).Distinct(StringComparer.Ordinal).Count() != edges.Count)
        {
            return ResultFactory.Failure<LayerDependencyEvaluation>(
                Problem.Validation("Layer dependency evaluation requires uniquely identified graph edges with valid endpoints, kinds, and confidence values."));
        }

        var membership = membershipResolver.Resolve(nodes, layers);
        if (!membership.IsSuccess)
        {
            return ResultFactory.Failure<LayerDependencyEvaluation>(membership.Problem!);
        }

        var membershipsByNode = membership.Value.ToDictionary(static resolution => resolution.NodeStableId, StringComparer.Ordinal);
        if (edges.Any(edge => !membershipsByNode.ContainsKey(edge.SourceStableId) || !membershipsByNode.ContainsKey(edge.TargetStableId)))
        {
            return ResultFactory.Failure<LayerDependencyEvaluation>(
                Problem.Validation("Layer dependency evaluation requires every graph-edge endpoint to have a layer membership result."));
        }

        var permissions = layers.ToDictionary(static layer => layer.Name, static layer => layer.MayDependOn.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var coverageIssues = membership.Value
            .Where(static membership => membership.State is LayerMembershipState.Unassigned or LayerMembershipState.Ambiguous)
            .Select(static membership => new LayerCoverageIssue(
                membership.NodeStableId,
                membership.State,
                membership.Detail ?? "The code node does not have a single deterministic layer assignment."))
            .OrderBy(static issue => issue.NodeStableId, StringComparer.Ordinal)
            .ToArray();
        var violations = new List<LayerDependencyViolation>();
        var eligibleEdges = edges.Where(edge => edgePolicy.IsEligible(edge, enforcement)).ToArray();
        foreach (var edge in eligibleEdges)
        {
            var sourceMembership = membershipsByNode[edge.SourceStableId];
            var targetMembership = membershipsByNode[edge.TargetStableId];
            if (sourceMembership.State != LayerMembershipState.Assigned ||
                targetMembership.State != LayerMembershipState.Assigned ||
                string.Equals(sourceMembership.LayerName, targetMembership.LayerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!permissions[sourceMembership.LayerName!].Contains(targetMembership.LayerName!))
            {
                violations.Add(new LayerDependencyViolation(
                    edge.EdgeId,
                    sourceMembership.LayerName!,
                    targetMembership.LayerName!,
                    edge,
                    $"Layer '{sourceMembership.LayerName}' may not depend on layer '{targetMembership.LayerName}' through {edge.EdgeKind} edge '{edge.EdgeId}'."));
            }
        }

        var cycleEligibleEdges = eligibleEdges
            .Where(edge =>
                membershipsByNode[edge.SourceStableId].State == LayerMembershipState.Assigned &&
                membershipsByNode[edge.TargetStableId].State == LayerMembershipState.Assigned)
            .ToArray();
        var cycles = cycleDetector.Detect(membership.Value, cycleEligibleEdges);
        if (!cycles.IsSuccess)
        {
            return ResultFactory.Failure<LayerDependencyEvaluation>(cycles.Problem!);
        }

        var reach = new EnforcementReach(
            eligibleEdges.Length,
            edges.Count,
            membership.Value
                .Where(static resolution => resolution.State == LayerMembershipState.Assigned && resolution.LayerName is not null)
                .Select(static resolution => resolution.LayerName!)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            [.. enforcement.HardEdgeKinds.Distinct(StringComparer.Ordinal).OrderBy(static kind => kind, StringComparer.Ordinal)],
            [.. edges.Select(static edge => edge.EdgeKind).Distinct(StringComparer.Ordinal).OrderBy(static kind => kind, StringComparer.Ordinal)]);

        return ResultFactory.Success(new LayerDependencyEvaluation(
            membership.Value,
            coverageIssues,
            cycles.Value,
            [.. violations.OrderBy(static violation => violation.EdgeId, StringComparer.Ordinal)],
            reach));
    }
}
