using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph.ReadGraphRevision;

public sealed class GraphRevisionSnapshotReaderTests
{
    [Fact]
    public async Task ReadsTheCompleteActiveRevisionInDeterministicOrder()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var source = GraphRevisionTestBuilder.Node("node:source", "hash:source");
        var target = GraphRevisionTestBuilder.Node("node:target", "hash:target");
        var edge = GraphRevisionTestBuilder.Edge("edge:source-target", source.StableId, target.StableId);
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [target, source], [edge]);
        var reader = new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System));

        var result = await reader.ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.NotNull(result.Value);
        Assert.Equal(revision, result.Value.Revision);
        Assert.Equal([source.StableId, target.StableId], result.Value.Nodes.Select(static node => node.StableId));
        Assert.Equal(edge, Assert.Single(result.Value.Edges));
    }

    [Fact]
    public async Task ReturnsNullWhenNoGraphRevisionIsActive()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var reader = new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System));

        var result = await reader.ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
