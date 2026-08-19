using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.UnitTests.Features.Architecture.VerificationBaselines;

public sealed class ArchitectureFindingFactoryTests
{
    private readonly ArchitectureFindingFactory factory = new();

    [Fact]
    public void ProducesStableFindingsForCoverageDirectionAndCycleEvidence()
    {
        var cycle = new ArchitectureDependencyCycle(
            ["node:application", "node:domain", "node:application"],
            ["edge:application-domain", "edge:domain-application"])
        {
            ComponentNodeStableIds = ["node:application", "node:domain", "node:other"],
        };
        var evaluation = new LayerDependencyEvaluation(
            [],
            [new LayerCoverageIssue("node:unassigned", LayerMembershipState.Unassigned, "No layer matches this source node.")],
            [cycle],
            [new LayerDependencyViolation(
                "edge:domain-application",
                "Domain",
                "Application",
                Edge("edge:domain-application", "node:domain", "node:application"),
                "Domain may not depend on Application.")],
            new EnforcementReach(1, 1, 1, ["references"], ["references"]));

        var result = factory.Create(evaluation);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Collection(
            result.Value,
            finding =>
            {
                Assert.Equal(ArchitectureFindingKind.DependencyCycle, finding.Kind);
                Assert.Equal("dependency-cycle|node:application|node:domain|node:other", finding.Key);
            },
            finding =>
            {
                Assert.Equal(ArchitectureFindingKind.LayerCoverage, finding.Kind);
                Assert.Equal("layer-coverage|unassigned|node:unassigned", finding.Key);
            },
            finding =>
            {
                Assert.Equal(ArchitectureFindingKind.LayerDependency, finding.Kind);
                Assert.Equal("layer-dependency|Domain|Application|calls|node:domain|node:application", finding.Key);
            });
    }

    [Fact]
    public void CollapsesMultipleCallSiteEdgesIntoOneStableLayerDependencyFinding()
    {
        var evaluation = new LayerDependencyEvaluation(
            [],
            [],
            [],
            [
                new LayerDependencyViolation(
                    "edge:z-call-site",
                    "Domain",
                    "Application",
                    Edge("edge:z-call-site", "node:domain", "node:application"),
                    "Domain may not depend on Application."),
                new LayerDependencyViolation(
                    "edge:a-call-site",
                    "Domain",
                    "Application",
                    Edge("edge:a-call-site", "node:domain", "node:application"),
                    "Domain may not depend on Application."),
            ],
                new EnforcementReach(1, 1, 1, ["references"], ["references"]));

        var result = factory.Create(evaluation);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var finding = Assert.Single(result.Value);
        Assert.Equal("layer-dependency|Domain|Application|calls|node:domain|node:application", finding.Key);
        Assert.Contains(finding.Targets, static target => target.StableId == "edge:a-call-site");
        Assert.DoesNotContain(finding.Targets, static target => target.StableId == "edge:z-call-site");
    }

    private static GraphEdgeFact Edge(string edgeId, string source, string target) => new(
        edgeId,
        source,
        target,
        "calls",
        null,
        "test",
        1,
        "{}");
}
