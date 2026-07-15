using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.UnitTests.Features.Architecture.DetectDependencyCycles;

public sealed class ArchitectureCycleDetectorTests
{
    private readonly ArchitectureCycleDetector detector = new();

    [Fact]
    public void SelectsTheShortestStableEvidenceCycleForEachStronglyConnectedComponent()
    {
        var result = detector.Detect(
            Membership("node:a", "node:b", "node:c"),
            [
                Edge("edge:a-b", "node:a", "node:b"),
                Edge("edge:b-a", "node:b", "node:a"),
                Edge("edge:b-c", "node:b", "node:c"),
                Edge("edge:c-a", "node:c", "node:a"),
            ]);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var cycle = Assert.Single(result.Value);
        Assert.Equal(["node:a", "node:b", "node:a"], cycle.NodePath);
        Assert.Equal(["edge:a-b", "edge:b-a"], cycle.EdgeIds);
    }

    [Fact]
    public void DetectsASelfReferentialHardEdge()
    {
        var result = detector.Detect(
            Membership("node:self"),
            [Edge("edge:self", "node:self", "node:self")]);

        Assert.True(result.IsSuccess);
        var cycle = Assert.Single(result.Value);
        Assert.Equal(["node:self", "node:self"], cycle.NodePath);
        Assert.Equal(["edge:self"], cycle.EdgeIds);
    }

    private static LayerMembershipResolution[] Membership(params string[] nodes) =>
        [.. nodes.Select(node => new LayerMembershipResolution(node, LayerMembershipState.Assigned, "Layer", ["Layer"], null))];

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
