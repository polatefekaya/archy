using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Duplicates.DuplicateFindings;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Duplicates;

public sealed class DuplicateFindingRepositoryTests
{
    [Fact]
    public async Task DuplicateObservationRejectsAnUndefinedSignalKindBeforeCreatingAFinding()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var store = new DuplicateFindingRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var result = await store.RecordObservationAsync(
            initialized.Value.StateLocation,
            new DuplicateFindingObservationFact(
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left"),
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:right"),
                1,
                "aggregate-v1",
                0.8,
                "{\"qualified\":true}",
                [new DuplicateSignalFact((DuplicateSignalKind)999, 0.8, "{\"tokens\":42}")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.Equal(
            0L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "duplicate_finding_identities"));
    }

    [Fact]
    public async Task DuplicateObservationRejectsRepeatedAlgorithmOutputWithoutAppendingSignals()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1"), GraphRevisionTestBuilder.Node("method:right", "hash:right:v1")]);
        var store = new DuplicateFindingRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var fact = new DuplicateFindingObservationFact(
            new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left"),
            new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:right"),
            revision,
            "aggregate-v1",
            0.8,
            "{\"qualified\":true}",
            [new DuplicateSignalFact(DuplicateSignalKind.Structural, 0.8, "{\"tokens\":42}")]);

        var first = await store.RecordObservationAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        Assert.True(first.IsSuccess);
        var repeated = await store.RecordObservationAsync(initialized.Value.StateLocation, fact, CancellationToken.None);

        Assert.False(repeated.IsSuccess);
        Assert.Equal("conflict", repeated.Problem!.Code);
        var history = await store.ListObservationsAsync(initialized.Value.StateLocation, first.Value.FindingId, CancellationToken.None);
        Assert.True(history.IsSuccess);
        Assert.Single(history.Value);
        Assert.Equal(
            1L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "duplicate_signal_observations"));
    }

    [Fact]
    public async Task DuplicateObservationHistoryRetainsSignalsAndLinksTargetedDecisions()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var firstRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1"), GraphRevisionTestBuilder.Node("method:right", "hash:right:v1")]);
        var secondRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v2"), GraphRevisionTestBuilder.Node("method:right", "hash:right:v1")]);
        var store = new DuplicateFindingRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var left = new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left");
        var right = new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:right");

        var first = await store.RecordObservationAsync(
            initialized.Value.StateLocation,
            new DuplicateFindingObservationFact(
                right,
                left,
                firstRevision,
                "aggregate-v1",
                0.62,
                "{\"qualified\":false}",
                [new DuplicateSignalFact(DuplicateSignalKind.Structural, 0.81, "{\"tokens\":42}")]),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await store.RecordObservationAsync(
            initialized.Value.StateLocation,
            new DuplicateFindingObservationFact(
                left,
                right,
                secondRevision,
                "aggregate-v1",
                0.89,
                "{\"qualified\":true}",
                [
                    new DuplicateSignalFact(DuplicateSignalKind.Structural, 0.91, "{\"tokens\":45}"),
                    new DuplicateSignalFact(DuplicateSignalKind.Semantic, 0.87, "{\"z_score\":2.1}"),
                ]),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.FindingId, second.Value.FindingId);

        var history = await store.ListObservationsAsync(initialized.Value.StateLocation, first.Value.FindingId, CancellationToken.None);
        Assert.True(history.IsSuccess);
        Assert.Collection(
            history.Value,
            observation =>
            {
                Assert.Equal(firstRevision, observation.GraphRevision);
                Assert.Equal("method:left", observation.LeftTarget.StableId);
                Assert.Equal("method:right", observation.RightTarget.StableId);
                Assert.Single(observation.Signals);
            },
            observation =>
            {
                Assert.Equal(secondRevision, observation.GraphRevision);
                Assert.Equal(0.89, observation.Confidence);
                Assert.Equal(2, observation.Signals.Count);
            });

        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var decision = await decisions.RecordAsync(
            initialized.Value.StateLocation,
            new ArchitectureDecisionFact(
                "duplicate_review",
                DecisionResolution.Modified,
                "Merged the useful behavior into the existing method.",
                "human",
                "fixture-user",
                null,
                secondRevision,
                [new ArchitectureTarget(ArchitectureTargetKind.DuplicateFinding, first.Value.FindingId)]),
            CancellationToken.None);
        Assert.True(decision.IsSuccess);

        var linked = await store.LinkResolutionAsync(
            initialized.Value.StateLocation,
            first.Value.FindingId,
            decision.Value.DecisionId,
            CancellationToken.None);
        Assert.True(linked.IsSuccess);

        var links = await store.ListResolutionLinksAsync(initialized.Value.StateLocation, first.Value.FindingId, CancellationToken.None);
        Assert.True(links.IsSuccess);
        var link = Assert.Single(links.Value);
        Assert.Equal(decision.Value.DecisionId, link.DecisionId);
    }
}
