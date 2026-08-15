using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Planning.ExplainArchitecture;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Planning.ExplainArchitecture;

public sealed class ArchitectureExplainerTests
{
    [Fact]
    public async Task SeparatesPersistedFactsDecisionsAndBoundedHistory()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Sessions/CreateSession.cs" };
        var dependency = GraphRevisionTestBuilder.Node("node:clock", "bbbbbbbbbbbbbbbb");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node, dependency], [GraphRevisionTestBuilder.Edge("edge:session-clock", node.StableId, dependency.StableId)]);
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, locks);
        var stored = await decisions.RecordAsync(initialized.Value.StateLocation, new("placement", DecisionResolution.Accepted, "Keep sessions isolated.", "agent", "test", null, revision, [new(ArchitectureTargetKind.GraphNode, node.StableId)]), CancellationToken.None);
        Assert.True(stored.IsSuccess);
        var explainer = new ArchitectureExplainer(new GraphRevisionSnapshotReader(locks), decisions);

        var result = await explainer.ExplainAsync(initialized.Value.StateLocation, new("CreateSession", 3), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal(node.StableId, result.Value!.ResolvedStableId);
        Assert.Contains(result.Value.Facts, fact => fact.Kind == "dependency" && fact.Provenance == "graph_edges");
        Assert.Contains(result.Value.Facts, fact => fact.Kind == "source" && fact.Detail.Contains("src/Sessions", StringComparison.Ordinal));
        Assert.Single(result.Value.Decisions);
        Assert.Empty(result.Value.AdvisoryContext);
        Assert.NotEmpty(result.Value.History);
    }

    [Fact]
    public async Task AbstainsWithActionableNotFoundForUnknownTarget()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var explainer = new ArchitectureExplainer(new GraphRevisionSnapshotReader(locks), new ArchitectureDecisionRepository(TimeProvider.System, locks));
        var result = await explainer.ExplainAsync(initialized.Value.StateLocation, new("missing"), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal("not_found", result.Problem!.Code);
    }
}
