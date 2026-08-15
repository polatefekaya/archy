using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Similarity.DetectReintroducedCapability;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.SharedKernel.Primitives;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Similarity.DetectReintroducedCapability;

public sealed class ReintroducedCapabilityDetectorTests
{
    [Fact]
    public async Task DetectsARecreatedRemovedCapabilityOnlyWithIndependentHistoricalEvidence()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var old = GraphRevisionTestBuilder.Node("old:create-session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/CreateSession.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [old]);
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, []);
        var current = GraphRevisionTestBuilder.Node("new:create-session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/CreateSession.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [current]);
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None); Assert.True(snapshot.IsSuccess);

        var result = await new ReintroducedCapabilityDetector(new WorkspaceLockManager(TimeProvider.System)).FindAsync(initialized.Value.StateLocation, snapshot.Value!, current.StableId, CancellationToken.None);

        Assert.True(result.IsSuccess); var match = Assert.Single(result.Value!.Matches); Assert.Equal(old.StableId, match.HistoricalStableId); Assert.Equal(current.StableId, match.CurrentStableId); Assert.Contains(match.Evidence, evidence => evidence.Kind == "content_hash");
    }

    [Fact]
    public async Task IncludesBoundedLinkedDecisionAndSessionProvenanceForTheRemovedCapability()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var old = GraphRevisionTestBuilder.Node("old:create-session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/CreateSession.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [old]);
        var locks = new WorkspaceLockManager(TimeProvider.System); var sessions = new ArchitectureSessionRepository(TimeProvider.System, locks);
        var started = await sessions.StartAsync(initialized.Value.StateLocation, new("history-session", "test", null, "agent", "test", "{}"), CancellationToken.None); Assert.True(started.IsSuccess);
        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, locks);
        var recorded = await decisions.RecordAsync(initialized.Value.StateLocation, new("preserve-capability", DecisionResolution.Accepted, null, "agent", "test", "history-session", 1, [new(ArchitectureTargetKind.GraphNode, old.StableId)]), CancellationToken.None); Assert.True(recorded.IsSuccess);
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, []);
        var current = GraphRevisionTestBuilder.Node("new:create-session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/CreateSession.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [current]);
        var snapshot = await new GraphRevisionSnapshotReader(locks).ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None);

        var result = await new ReintroducedCapabilityDetector(locks).FindAsync(initialized.Value.StateLocation, snapshot.Value!, current.StableId, CancellationToken.None);

        Assert.True(result.IsSuccess); var match = Assert.Single(result.Value!.Matches); Assert.Contains(recorded.Value!.DecisionId, match.DecisionIds); Assert.Contains("history-session", match.SessionIds);
    }
}
