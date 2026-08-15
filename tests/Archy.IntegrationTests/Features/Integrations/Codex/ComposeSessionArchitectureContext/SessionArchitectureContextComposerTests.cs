using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Integrations.Codex.ComposeSessionArchitectureContext;
using Archy.Features.Similarity.BuildSimilarityClusters;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Integrations.Codex.ComposeSessionArchitectureContext;

public sealed class SessionArchitectureContextComposerTests
{
    [Fact]
    public async Task ProducesBoundedPersistedGraphContextWithoutAmodelRequest()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:public", "aaaaaaaaaaaaaaaa") with { FilePath = "src/Public.cs" };
        var symbol = new GraphSymbolFact("symbol:public", node.StableId, "Sample.Public", "public", "Sample.Public()", "[]", "{}", "aaaaaaaaaaaaaaaa");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node], symbols: [symbol]);
        var result = await new SessionArchitectureContextComposer(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).ComposeAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message); Assert.Equal(1, result.Value!.GraphRevision); Assert.Contains("src/Public.cs", result.Value.FocusedPaths); Assert.Contains(node.StableId, result.Value.PublicSurfaceStableIds); Assert.True(result.Value.Text.Length <= 2000);
    }

    [Fact]
    public async Task AbstainsCleanlyWithoutAnActiveGraph()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var result = await new SessionArchitectureContextComposer(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).ComposeAsync(initialized.Value.StateLocation, CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Equal(0, result.Value!.GraphRevision); Assert.NotEmpty(result.Value.Abstentions);
    }

    [Fact]
    public async Task IncludesOnlyBoundedDecisionReferencesForPublicContextTargets()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:public", "aaaaaaaaaaaaaaaa") with { FilePath = "src/Public.cs" };
        var symbol = new GraphSymbolFact("symbol:public", node.StableId, "Sample.Public", "public", "Sample.Public()", "[]", "{}", "aaaaaaaaaaaaaaaa");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node], symbols: [symbol]);
        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var recorded = await decisions.RecordAsync(initialized.Value.StateLocation, new("public-contract", DecisionResolution.Accepted, "private note", "agent", "test", null, 1, [new(ArchitectureTargetKind.GraphNode, node.StableId)]), CancellationToken.None); Assert.True(recorded.IsSuccess);

        var result = await new SessionArchitectureContextComposer(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).ComposeAsync(initialized.Value.StateLocation, CancellationToken.None);

        Assert.True(result.IsSuccess); Assert.Contains(recorded.Value!.DecisionId, result.Value!.DecisionIds); Assert.DoesNotContain("private note", result.Value.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesOnlyClustersFromTheActiveGraphRevisionThatTouchPublicSurface()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var first = GraphRevisionTestBuilder.Node("first", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/First.cs" };
        var second = GraphRevisionTestBuilder.Node("second", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Second.cs" };
        var shared = GraphRevisionTestBuilder.Node("shared", "cccccccccccccccc");
        var symbol = new GraphSymbolFact("symbol:first", first.StableId, "Sample.CreateSession", "public", "Sample.CreateSession()", "[]", "{}", "aaaaaaaaaaaaaaaa");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [first, second, shared], [GraphRevisionTestBuilder.Edge("first-shared", "first", "shared"), GraphRevisionTestBuilder.Edge("second-shared", "second", "shared")], [symbol]);
        var locks = new WorkspaceLockManager(TimeProvider.System); var snapshot = await new GraphRevisionSnapshotReader(locks).ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None);
        var stored = await new SimilarityClusterRepository(TimeProvider.System, locks).RecordAsync(initialized.Value.StateLocation, SimilarityClusterBuilder.Build(snapshot.Value!), CancellationToken.None); Assert.True(stored.IsSuccess);

        var result = await new SessionArchitectureContextComposer(new GraphRevisionSnapshotReader(locks)).ComposeAsync(initialized.Value.StateLocation, CancellationToken.None);

        Assert.True(result.IsSuccess); Assert.Equal([stored.Value!.Clusters.Single().Id], result.Value!.SimilarityClusterIds);
    }
}
