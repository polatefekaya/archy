using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Keeps finding identities independent of volatile graph edge locations and explanatory-path choices.</summary>
public sealed class ArchitectureFindingFactory : IArchitectureFindingFactory
{
    public Result<IReadOnlyList<ArchitectureFinding>> Create(LayerDependencyEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.CoverageIssues is null || evaluation.Violations is null || evaluation.Cycles is null)
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureFinding>>(
                Problem.Validation("Architecture findings require complete coverage, direction, and cycle evaluation output."));
        }

        var findings = new List<ArchitectureFinding>();
        if (evaluation.Reach is { IsUnenforceable: true })
        {
            findings.Add(EnforcementUnavailable(evaluation.Reach));
        }

        findings.AddRange(evaluation.CoverageIssues.Select(Coverage));
        findings.AddRange(evaluation.Violations.Select(Dependency));
        findings.AddRange(evaluation.Cycles.Select(Cycle));
        if (findings.Any(static finding => finding.Targets.Length == 0 || string.IsNullOrWhiteSpace(finding.Key)))
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureFinding>>(
                Problem.Validation("Architecture evaluation produced an incomplete stable finding identity."));
        }

        return ResultFactory.Success<IReadOnlyList<ArchitectureFinding>>(
            [.. findings
                .GroupBy(static finding => finding.Key, StringComparer.Ordinal)
                .Select(static group => group
                    .OrderBy(static finding => string.Join(
                        "\u001f",
                        finding.Targets.Select(static target => $"{target.Kind}:{target.StableId}")),
                        StringComparer.Ordinal)
                    .First())
                .OrderBy(static finding => finding.Key, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Reports that the configured gate cannot fail, so a passing run proves nothing about the code.
    /// </summary>
    /// <remarks>
    /// This is a deterministic, fully certain statement about the evaluated revision — the same
    /// standard every other blocking finding meets — so it blocks rather than warns. A warning
    /// here would be read as a passing gate, which is the outcome it exists to prevent.
    /// </remarks>
    private static ArchitectureFinding EnforcementUnavailable(EnforcementReach reach)
    {
        ArgumentNullException.ThrowIfNull(reach);
        var configured = reach.ConfiguredEdgeKinds.Count == 0
            ? "none"
            : string.Join(", ", reach.ConfiguredEdgeKinds);
        var observed = reach.ObservedEdgeKinds.Count == 0
            ? "none"
            : string.Join(", ", reach.ObservedEdgeKinds);
        var message =
            $"No graph edge satisfies the configured hard-edge policy, so no layer violation or dependency cycle can be reported. " +
            $"Enforcement reads only confidence-1.0 edges of kind: {configured}. " +
            $"This revision has {reach.TotalEdgeCount} edge(s) of kind: {observed}. " +
            "Semantic analysis is usually missing: run 'archy doctor --path .' and install the language server its blocking check names, then re-run 'archy analyze'.";
        return new ArchitectureFinding(
            "enforcement-unavailable|hard-edge-policy",
            ArchitectureFindingKind.EnforcementUnavailable,
            message,
            [new ArchitectureTarget(ArchitectureTargetKind.Rule, "enforcement-unavailable:hard-edge-policy")]);
    }

    private static ArchitectureFinding Coverage(LayerCoverageIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var key = $"layer-coverage|{issue.State.ToString().ToLowerInvariant()}|{issue.NodeStableId}";
        return new ArchitectureFinding(
            key,
            ArchitectureFindingKind.LayerCoverage,
            issue.Message,
            [
                new ArchitectureTarget(ArchitectureTargetKind.Rule, $"layer-coverage:{issue.State.ToString().ToLowerInvariant()}"),
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, issue.NodeStableId),
            ]);
    }

    private static ArchitectureFinding Dependency(LayerDependencyViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        var edge = violation.Edge ?? throw new ArgumentException("Layer dependency violation requires its graph edge.", nameof(violation));
        var key = $"layer-dependency|{violation.SourceLayer}|{violation.TargetLayer}|{edge.EdgeKind}|{edge.SourceStableId}|{edge.TargetStableId}";
        return new ArchitectureFinding(
            key,
            ArchitectureFindingKind.LayerDependency,
            violation.Message,
            [
                new ArchitectureTarget(ArchitectureTargetKind.Rule, $"layer-dependency:{violation.SourceLayer}->{violation.TargetLayer}"),
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, edge.SourceStableId),
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, edge.TargetStableId),
                new ArchitectureTarget(ArchitectureTargetKind.GraphEdge, edge.EdgeId),
            ]);
    }

    private static ArchitectureFinding Cycle(DetectDependencyCycles.ArchitectureDependencyCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        var componentNodes = cycle.ComponentNodeStableIds
            .OrderBy(static node => node, StringComparer.Ordinal)
            .ToArray();
        if (componentNodes.Length == 0)
        {
            throw new ArgumentException("Dependency cycle requires at least one component node.", nameof(cycle));
        }

        var key = $"dependency-cycle|{string.Join("|", componentNodes)}";
        var targets = new List<ArchitectureTarget>
        {
            new(ArchitectureTargetKind.Rule, "dependency-cycle"),
        };
        targets.AddRange(componentNodes.Select(static node => new ArchitectureTarget(ArchitectureTargetKind.GraphNode, node)));
        targets.AddRange(cycle.EdgeIds
            .OrderBy(static edgeId => edgeId, StringComparer.Ordinal)
            .Select(static edgeId => new ArchitectureTarget(ArchitectureTargetKind.GraphEdge, edgeId)));
        return new ArchitectureFinding(
            key,
            ArchitectureFindingKind.DependencyCycle,
            $"Hard dependency cycle: {string.Join(" -> ", cycle.NodePath)}.",
            [.. targets]);
    }
}
