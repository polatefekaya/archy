using Archy.Features.Placement.ClusterRevisions;
using Archy.Features.Placement.ReadClusterMemberPages;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Placement.ReadClusterMemberPages;

public sealed class ClusterMemberPageReaderTests
{
    [Fact]
    public async Task PagesOnlyTheMembersOfTheRequestedImmutableClusterRevision()
    {
        using var fixture=WorkspaceStateFixture.Create();var initialized=await fixture.InitializeAsync();Assert.True(initialized.IsSuccess);
        var revision=await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation,[GraphRevisionTestBuilder.Node("node:a","a"),GraphRevisionTestBuilder.Node("node:b","b")]);
        var stored=await new ClusterRevisionRepository(TimeProvider.System,new WorkspaceLockManager(TimeProvider.System)).RecordAsync(initialized.Value.StateLocation,new ClusterRevisionFact(revision,"algorithm","1","input",[new ClusterFact("key",[new ClusterMemberFact(new ArchitectureTarget(ArchitectureTargetKind.GraphNode,"node:a"),.9),new ClusterMemberFact(new ArchitectureTarget(ArchitectureTargetKind.GraphNode,"node:b"),.8)])]),CancellationToken.None);Assert.True(stored.IsSuccess);
        var cluster=Assert.Single(stored.Value.Clusters);var page=await new ClusterMemberPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation,stored.Value.ClusterRevisionId,cluster.ClusterId,1,1,CancellationToken.None);
        Assert.True(page.IsSuccess,page.IsSuccess?string.Empty:page.Problem!.Message);Assert.Equal(revision,page.Value.GraphRevision);Assert.Equal(2,page.Value.TotalCount);Assert.Equal("node:b",Assert.Single(page.Value.Items).Target.StableId);
    }
}
