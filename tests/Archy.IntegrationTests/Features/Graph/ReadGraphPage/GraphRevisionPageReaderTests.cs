using Archy.Features.Graph.ReadGraphPage;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph.ReadGraphPage;

public sealed class GraphRevisionPageReaderTests
{
    [Fact]
    public async Task ReadsDeterministicBoundedPagesFromTheRequestedHistoricalRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var nodes = Enumerable.Range(0, 4).Select(index => GraphRevisionTestBuilder.Node($"node:{index}", $"hash:{index}:v1")).ToArray();
        var first = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            nodes,
            [GraphRevisionTestBuilder.Edge("edge:0-1", "node:0", "node:1"), GraphRevisionTestBuilder.Edge("edge:1-2", "node:1", "node:2")]);
        _ = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [nodes[0]], []);
        var reader = new GraphRevisionPageReader(new WorkspaceLockManager(TimeProvider.System));

        var page = await reader.ReadPageAsync(
            initialized.Value.StateLocation,
            new GraphRevisionPageQuery(GraphRevisionFactKind.Nodes, first, Offset: 1, Limit: 2),
            CancellationToken.None);

        Assert.True(page.IsSuccess, page.IsSuccess ? string.Empty : page.Problem!.Message);
        Assert.Equal(first, page.Value.Revision);
        Assert.Equal(4, page.Value.TotalCount);
        Assert.Equal(["node:1", "node:2"], page.Value.Nodes.Select(static node => node.StableId));
        Assert.Empty(page.Value.Edges);
    }

    [Fact]
    public async Task ReadsMetadataAndRejectsInvalidOrUnavailableSelectionsBeforeReturningFacts()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var reader = new GraphRevisionPageReader(new WorkspaceLockManager(TimeProvider.System));

        var absent = await reader.ReadMetadataAsync(initialized.Value.StateLocation, null, CancellationToken.None);
        var invalid = await reader.ReadPageAsync(initialized.Value.StateLocation, new GraphRevisionPageQuery(GraphRevisionFactKind.Nodes, null, -1, 1), CancellationToken.None);
        var unknown = await reader.ReadPageAsync(initialized.Value.StateLocation, new GraphRevisionPageQuery(GraphRevisionFactKind.Edges, 999, 0, 1), CancellationToken.None);

        Assert.True(absent.IsSuccess);
        Assert.Null(absent.Value);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("validation", invalid.Problem!.Code);
        Assert.False(unknown.IsSuccess);
        Assert.Equal("not_found", unknown.Problem!.Code);
    }
}
