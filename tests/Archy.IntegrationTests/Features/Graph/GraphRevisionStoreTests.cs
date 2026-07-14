using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Storage.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph;

public sealed class GraphRevisionStoreTests
{
    [Fact]
    public async Task CommitPersistsACompleteImmutableGraphRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var run = await StartRunAsync(initialized.Value.StateLocation);
        var nodes = new[]
        {
            new GraphNodeFact("file:program", "file", "Program.cs", "Program.cs", "Program.cs", 1, 10, "fixture", 1, "{\"kind\":\"file\"}"),
            new GraphNodeFact("type:program", "type", "Archy.Program", "Program", "Program.cs", 3, 10, "fixture", 0.95, "{\"kind\":\"symbol\"}"),
        };
        var edge = new GraphEdgeFact(
            "contains:file:program:type:program",
            "file:program",
            "type:program",
            "contains",
            "program.cs#type:archy.program",
            "fixture",
            1,
            "{\"source\":\"Program.cs:3\"}");

        var store = new GraphRevisionStore(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var committed = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            nodes,
            [edge],
            CancellationToken.None);

        Assert.True(committed.IsSuccess);
        Assert.Equal(1L, committed.Value.Revision);
        Assert.Equal(2, committed.Value.NodeCount);
        Assert.Equal(1, committed.Value.EdgeCount);

        var duplicateCommit = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            nodes,
            [edge],
            CancellationToken.None);
        Assert.False(duplicateCommit.IsSuccess);
        Assert.Equal("conflict", duplicateCommit.Problem!.Code);

        await SqliteAssertions.AssertGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            committed.Value.Revision,
            run.RunId,
            expectedNodeCount: 2,
            expectedEdgeCount: 1);
        await SqliteAssertions.AssertGraphEdgeAsync(
            initialized.Value.StateLocation.DatabasePath,
            committed.Value.Revision,
            edge.EdgeId,
            expectedJoinKey: edge.NormalizedJoinKey!,
            expectedEvidenceJson: edge.EvidenceJson);
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            run.RunId,
            expectedStatus: "succeeded",
            expectedGraphRevision: committed.Value.Revision,
            expectedEvents: ["started", "succeeded"]);
    }

    [Fact]
    public async Task CommitRejectsInvalidFactsWithoutExposingAPartialRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var run = await StartRunAsync(initialized.Value.StateLocation);
        var node = new GraphNodeFact("file:program", "file", "Program.cs", "Program.cs", "Program.cs", 1, 1, "fixture", 1, "{}");
        var invalidEdge = new GraphEdgeFact("invalid", "file:program", "missing:node", "calls", null, "fixture", 1, "{}");
        var store = new GraphRevisionStore(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var rejected = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [node],
            [invalidEdge],
            CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation", rejected.Problem!.Code);
        Assert.Equal(
            0L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            run.RunId,
            expectedStatus: "running",
            expectedGraphRevision: null,
            expectedEvents: ["started"]);

        var recovered = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [node],
            [],
            CancellationToken.None);
        Assert.True(recovered.IsSuccess);
    }

    private static async Task<AnalysisRun> StartRunAsync(
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location)
    {
        var started = await new AnalysisRunStore(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: null,
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        return started.Value;
    }
}
