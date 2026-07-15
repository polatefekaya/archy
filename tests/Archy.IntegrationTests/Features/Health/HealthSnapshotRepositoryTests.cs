using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Health.HealthSnapshots;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Health;

public sealed class HealthSnapshotRepositoryTests
{
    [Fact]
    public async Task HealthSnapshotPreservesItsComponentAndDecisionInputs()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var graphRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1")]);
        var decision = await new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).RecordAsync(
            initialized.Value.StateLocation,
            new ArchitectureDecisionFact(
                "architecture_exception_review",
                DecisionResolution.Accepted,
                "Narrow exception approved for the current revision.",
                "human",
                "fixture-user",
                null,
                graphRevision,
                [new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left")]),
            CancellationToken.None);
        Assert.True(decision.IsSuccess);
        var store = new HealthSnapshotRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var fact = new HealthSnapshotFact(
            graphRevision,
            "health-v1",
            91.5,
            [
                new HealthMetricComponentFact("introduced_violations", 0, 0.45, 0, "{\"count\":0}"),
                new HealthMetricComponentFact("decision_resolution_rate", 1, 0.2, 20, "{\"accepted\":1}"),
            ],
            [decision.Value.DecisionId]);

        var recorded = await store.RecordAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        Assert.True(recorded.IsSuccess);
        Assert.Equal(91.5, recorded.Value.Score);

        var loaded = await store.GetAsync(initialized.Value.StateLocation, recorded.Value.HealthSnapshotId, CancellationToken.None);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(recorded.Value.HealthSnapshotId, loaded.Value.HealthSnapshotId);
        Assert.Equal(recorded.Value.GraphRevision, loaded.Value.GraphRevision);
        Assert.Equal(recorded.Value.CalculationVersion, loaded.Value.CalculationVersion);
        Assert.Equal(recorded.Value.Score, loaded.Value.Score);
        Assert.Equal(recorded.Value.Components, loaded.Value.Components);
        Assert.Equal(recorded.Value.ResolutionDecisionIds, loaded.Value.ResolutionDecisionIds);

        var duplicate = await store.RecordAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("conflict", duplicate.Problem!.Code);
    }

    [Fact]
    public async Task HealthSnapshotRejectsAnUnknownResolutionDecision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var graphRevision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("method:left", "hash:left:v1")]);

        var rejected = await new HealthSnapshotRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).RecordAsync(
            initialized.Value.StateLocation,
            new HealthSnapshotFact(
                graphRevision,
                "health-v1",
                90,
                [new HealthMetricComponentFact("introduced_violations", 0, 0.45, 0, "{\"count\":0}")],
                ["missing-decision"]),
            CancellationToken.None);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);
    }
}
