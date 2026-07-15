using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.UnitTests.Features.Architecture.EnforceLayerDependencies;

public sealed class LayerDependencyRuleEvaluatorTests
{
    private readonly LayerDependencyRuleEvaluator evaluator = new(
        new LayerMembershipResolver(),
        new HardArchitectureEdgePolicy(),
        new ArchitectureCycleDetector());

    [Fact]
    public void ReportsConfiguredHardEdgesThatViolateLayerDirection()
    {
        var result = evaluator.Evaluate(
            [Node("node:domain", "src/Domain/Order.cs"), Node("node:presentation", "src/Presentation/OrderController.cs")],
            [Edge("edge:bad", "node:domain", "node:presentation", "calls", 1)],
            Layers(),
            new ArchitectureEnforcementConfiguration(["calls", "references"]));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var violation = Assert.Single(result.Value.Violations);
        Assert.Equal("Domain", violation.SourceLayer);
        Assert.Equal("Presentation", violation.TargetLayer);
        Assert.Equal("edge:bad", violation.EdgeId);
    }

    [Fact]
    public void AllowsConfiguredLayerDirectionAndSameLayerDependencies()
    {
        var result = evaluator.Evaluate(
            [Node("node:application", "src/Application/OrderHandler.cs"), Node("node:domain", "src/Domain/Order.cs"), Node("node:domain2", "src/Domain/Clock.cs")],
            [
                Edge("edge:allowed", "node:application", "node:domain", "calls", 1),
                Edge("edge:same", "node:domain", "node:domain2", "calls", 1),
            ],
            Layers(),
            new ArchitectureEnforcementConfiguration(["calls"]));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Violations);
    }

    [Fact]
    public void NeverTurnsLowConfidenceProviderEdgesIntoHardViolations()
    {
        var result = evaluator.Evaluate(
            [Node("node:domain", "src/Domain/Order.cs"), Node("node:presentation", "src/Presentation/OrderController.cs")],
            [Edge("edge:advisory", "node:domain", "node:presentation", "di_consumes", 0.8)],
            Layers(),
            new ArchitectureEnforcementConfiguration(["di_consumes"]));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Violations);
    }

    [Fact]
    public void LeavesAmbiguousMembershipVisibleWithoutInventingAViolation()
    {
        var result = evaluator.Evaluate(
            [Node("node:ambiguous", "src/Presentation/Shared/Order.cs"), Node("node:domain", "src/Domain/Order.cs")],
            [Edge("edge:ambiguous", "node:ambiguous", "node:domain", "calls", 1)],
            [
                new LayerRuleConfiguration("Presentation", ["src/Presentation/**"], ["Domain"]),
                new LayerRuleConfiguration("Shared", ["src/Presentation/Shared/**"], []),
                new LayerRuleConfiguration("Domain", ["src/Domain/**"], []),
            ],
            new ArchitectureEnforcementConfiguration(["calls"]));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Violations);
        Assert.Contains(result.Value.Membership, static membership => membership.NodeStableId == "node:ambiguous" && membership.State == LayerMembershipState.Ambiguous);
        var coverage = Assert.Single(result.Value.CoverageIssues);
        Assert.Equal("node:ambiguous", coverage.NodeStableId);
        Assert.Equal(LayerMembershipState.Ambiguous, coverage.State);
    }

    [Fact]
    public void ReportsHardDependencyCyclesEvenWhenEveryIndividualLayerDirectionIsAllowed()
    {
        var result = evaluator.Evaluate(
            [Node("node:domain-a", "src/Domain/A.cs"), Node("node:domain-b", "src/Domain/B.cs")],
            [
                Edge("edge:a-b", "node:domain-a", "node:domain-b", "calls", 1),
                Edge("edge:b-a", "node:domain-b", "node:domain-a", "calls", 1),
            ],
            Layers(),
            new ArchitectureEnforcementConfiguration(["calls"]));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Empty(result.Value.Violations);
        var cycle = Assert.Single(result.Value.Cycles);
        Assert.Equal(["edge:a-b", "edge:b-a"], cycle.EdgeIds);
    }

    private static LayerRuleConfiguration[] Layers() =>
    [
        new LayerRuleConfiguration("Presentation", ["src/Presentation/**"], ["Application"]),
        new LayerRuleConfiguration("Application", ["src/Application/**"], ["Domain"]),
        new LayerRuleConfiguration("Domain", ["src/Domain/**"], []),
    ];

    private static GraphNodeFact Node(string stableId, string path) => new(
        stableId,
        "semantic_type",
        stableId,
        stableId,
        path,
        1,
        1,
        "test",
        1,
        "{}",
        "ABC");

    private static GraphEdgeFact Edge(string edgeId, string source, string target, string kind, double confidence) => new(
        edgeId,
        source,
        target,
        kind,
        null,
        "test",
        confidence,
        "{}");
}
