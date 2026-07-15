using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.UnitTests.Features.Architecture.LayerMembership;

public sealed class LayerMembershipResolverTests
{
    private readonly LayerMembershipResolver resolver = new();

    [Fact]
    public void ResolvesAssignedUnassignedAmbiguousAndVirtualNodesDeterministically()
    {
        var result = resolver.Resolve(
            [
                Node("node:application", "src/App/Order.cs"),
                Node("node:ambiguous", "src/App/Shared/Clock.cs"),
                Node("node:unassigned", "tests/OrderTests.cs"),
                Node("node:virtual", null),
            ],
            [
                new LayerRuleConfiguration("Application", ["src/App/**"], []),
                new LayerRuleConfiguration("Shared", ["src/App/Shared/**"], []),
            ]);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Collection(
            result.Value,
            resolution => Assert.Equal(("node:ambiguous", LayerMembershipState.Ambiguous), (resolution.NodeStableId, resolution.State)),
            resolution => Assert.Equal(("node:application", LayerMembershipState.Assigned, "Application"), (resolution.NodeStableId, resolution.State, resolution.LayerName)),
            resolution => Assert.Equal(("node:unassigned", LayerMembershipState.Unassigned), (resolution.NodeStableId, resolution.State)),
            resolution => Assert.Equal(("node:virtual", LayerMembershipState.NotApplicable), (resolution.NodeStableId, resolution.State)));
    }

    [Fact]
    public void AppliesFileNamePatternsAcrossRepositoryDirectories()
    {
        var result = resolver.Resolve(
            [Node("node:service", "src/Features/Orders/OrderService.cs")],
            [new LayerRuleConfiguration("Services", ["*Service.cs"], [])]);

        Assert.True(result.IsSuccess);
        var membership = Assert.Single(result.Value);
        Assert.Equal(LayerMembershipState.Assigned, membership.State);
        Assert.Equal("Services", membership.LayerName);
    }

    [Fact]
    public void RejectsNodesWithPathsOutsideTheRepository()
    {
        var result = resolver.Resolve(
            [Node("node:outside", "../outside/Order.cs")],
            [new LayerRuleConfiguration("Application", ["src/**"], [])]);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }

    private static GraphNodeFact Node(string stableId, string? path) => new(
        stableId,
        "semantic_type",
        stableId,
        stableId,
        path,
        path is null ? null : 1,
        path is null ? null : 1,
        "test",
        1,
        "{}",
        "ABC");
}
