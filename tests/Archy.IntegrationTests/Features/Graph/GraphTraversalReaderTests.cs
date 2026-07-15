using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph;

public sealed class GraphTraversalReaderTests
{
    [Fact]
    public async Task TraversesDependenciesAndDependentsWithRevisionScopedProvenance()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitGraphAsync(initialized.Value.StateLocation);
        var store = new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System));

        var dependencies = await store.TraverseAsync(
            initialized.Value.StateLocation,
            new GraphTraversalQuery("node:a", GraphTraversalDirection.Dependencies, revision, MaxDepth: 6),
            CancellationToken.None);
        Assert.True(dependencies.IsSuccess);
        Assert.False(dependencies.Value.IsTruncated);
        Assert.Collection(
            dependencies.Value.Edges,
            edge =>
            {
                Assert.Equal(1, edge.Depth);
                Assert.Equal("edge:a-b", edge.EdgeId);
                Assert.Equal("node:a", edge.SourceStableId);
                Assert.Equal("node:b", edge.TargetStableId);
                Assert.Equal("fixture", edge.Provider);
            },
            edge =>
            {
                Assert.Equal(2, edge.Depth);
                Assert.Equal("edge:b-c", edge.EdgeId);
            });

        var dependents = await store.TraverseAsync(
            initialized.Value.StateLocation,
            new GraphTraversalQuery("node:b", GraphTraversalDirection.Dependents, revision, MaxDepth: 6),
            CancellationToken.None);
        Assert.True(dependents.IsSuccess);
        Assert.Collection(
            dependents.Value.Edges,
            edge => Assert.Equal("edge:a-b", edge.EdgeId),
            edge => Assert.Equal("edge:d-b", edge.EdgeId),
            edge =>
            {
                Assert.Equal(2, edge.Depth);
                Assert.Equal("edge:c-a", edge.EdgeId);
            });
    }

    [Fact]
    public async Task UsesTheSelectedHistoricalRevisionAndMarksBoundedResultsAsTruncated()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var nodes = new[]
        {
            GraphRevisionTestBuilder.Node("node:a", "hash:a:v1"),
            GraphRevisionTestBuilder.Node("node:b", "hash:b:v1"),
            GraphRevisionTestBuilder.Node("node:c", "hash:c:v1"),
            GraphRevisionTestBuilder.Node("node:d", "hash:d:v1"),
        };
        var firstRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            nodes,
            [
                GraphRevisionTestBuilder.Edge("edge:a-b", "node:a", "node:b"),
                GraphRevisionTestBuilder.Edge("edge:a-c", "node:a", "node:c"),
            ]);
        var secondRevision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, nodes, []);
        var store = new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System));

        var current = await store.TraverseAsync(
            initialized.Value.StateLocation,
            new GraphTraversalQuery("node:a", GraphTraversalDirection.Dependencies),
            CancellationToken.None);
        Assert.True(current.IsSuccess);
        Assert.Equal(secondRevision, current.Value.Revision);
        Assert.Empty(current.Value.Edges);

        var historical = await store.TraverseAsync(
            initialized.Value.StateLocation,
            new GraphTraversalQuery("node:a", GraphTraversalDirection.Dependencies, firstRevision, MaxEdges: 1),
            CancellationToken.None);
        Assert.True(historical.IsSuccess);
        Assert.True(historical.Value.IsTruncated);
        var edge = Assert.Single(historical.Value.Edges);
        Assert.Equal(1, edge.Depth);
        Assert.Equal(firstRevision, historical.Value.Revision);
    }

    [Fact]
    public async Task ActiveGraphLookupsUseAnIndexInsteadOfAScan()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        _ = await CommitGraphAsync(initialized.Value.StateLocation);

        var plan = await SqliteTestDatabase.GetQueryPlanAsync(
            initialized.Value.StateLocation.DatabasePath,
            $"SELECT edge_id FROM graph_edge_versions WHERE workspace_id = '{initialized.Value.StateLocation.WorkspaceId}' AND valid_to_revision IS NULL AND source_stable_id = 'node:a';");

        Assert.Contains(plan, detail => detail.Contains("SEARCH graph_edge_versions USING INDEX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TraversalRejectsAnUnknownDirectionBeforeOpeningState()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var rejected = await new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System)).TraverseAsync(
            initialized.Value.StateLocation,
            new GraphTraversalQuery("node:a", (GraphTraversalDirection)999),
            CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation", rejected.Problem!.Code);
    }

    private static Task<long> CommitGraphAsync(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location) =>
        GraphRevisionTestBuilder.CommitAsync(
            location,
            [
                GraphRevisionTestBuilder.Node("node:a", "hash:a:v1"),
                GraphRevisionTestBuilder.Node("node:b", "hash:b:v1"),
                GraphRevisionTestBuilder.Node("node:c", "hash:c:v1"),
                GraphRevisionTestBuilder.Node("node:d", "hash:d:v1"),
            ],
            [
                GraphRevisionTestBuilder.Edge("edge:a-b", "node:a", "node:b"),
                GraphRevisionTestBuilder.Edge("edge:b-c", "node:b", "node:c"),
                GraphRevisionTestBuilder.Edge("edge:c-a", "node:c", "node:a"),
                GraphRevisionTestBuilder.Edge("edge:d-b", "node:d", "node:b"),
            ]);
}
