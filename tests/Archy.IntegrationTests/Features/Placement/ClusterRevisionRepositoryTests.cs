using Archy.Features.Placement.ClusterRevisions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Placement;

public sealed class ClusterRevisionRepositoryTests
{
    [Fact]
    public async Task ClusterRevisionPersistsAnImmutablePartitionOfGraphTargets()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var graphRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1"), GraphRevisionTestBuilder.Node("method:right", "hash:right:v1")]);
        var store = new ClusterRevisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var fact = new ClusterRevisionFact(
            graphRevision,
            "louvain",
            "1.0.0",
            "input-hash-v1",
            [new ClusterFact(
                "application",
                [
                    new ClusterMemberFact(new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left"), 0.91),
                    new ClusterMemberFact(new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:right"), 0.73),
                ])]);

        var recorded = await store.RecordAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        Assert.True(recorded.IsSuccess);
        var cluster = Assert.Single(recorded.Value.Clusters);
        Assert.Equal("application", cluster.ClusterKey);
        Assert.Equal(2, cluster.Members.Count);

        var loaded = await store.GetAsync(initialized.Value.StateLocation, recorded.Value.ClusterRevisionId, CancellationToken.None);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(recorded.Value.ClusterRevisionId, loaded.Value.ClusterRevisionId);
        Assert.Equal(recorded.Value.GraphRevision, loaded.Value.GraphRevision);
        Assert.Equal(recorded.Value.Algorithm, loaded.Value.Algorithm);
        Assert.Equal(recorded.Value.AlgorithmVersion, loaded.Value.AlgorithmVersion);
        Assert.Equal(recorded.Value.InputHash, loaded.Value.InputHash);
        Assert.Collection(
            loaded.Value.Clusters,
            loadedCluster =>
            {
                Assert.Equal(cluster.ClusterId, loadedCluster.ClusterId);
                Assert.Equal(cluster.ClusterKey, loadedCluster.ClusterKey);
                Assert.Equal(cluster.Members, loadedCluster.Members);
            });

        var duplicate = await store.RecordAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("conflict", duplicate.Problem!.Code);
    }

    [Fact]
    public async Task ClusterRevisionRejectsARepeatedMemberAcrossClusters()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var graphRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1"), GraphRevisionTestBuilder.Node("method:right", "hash:right:v1")]);
        var repeatedTarget = new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left");

        var rejected = await new ClusterRevisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).RecordAsync(
            initialized.Value.StateLocation,
            new ClusterRevisionFact(
                graphRevision,
                "louvain",
                "1.0.0",
                "input-hash-v1",
                [
                    new ClusterFact("first", [new ClusterMemberFact(repeatedTarget, 1)]),
                    new ClusterFact("second", [new ClusterMemberFact(repeatedTarget, 1)]),
                ]),
            CancellationToken.None);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation", rejected.Problem!.Code);
    }
}
