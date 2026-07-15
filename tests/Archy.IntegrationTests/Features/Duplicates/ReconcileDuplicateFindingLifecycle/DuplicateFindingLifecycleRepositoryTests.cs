using Archy.Features.Duplicates.DuplicateFindings;
using Archy.Features.Duplicates.ReconcileDuplicateFindingLifecycle;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Duplicates.ReconcileDuplicateFindingLifecycle;

public sealed class DuplicateFindingLifecycleRepositoryTests
{
    [Fact]
    public async Task ReconcileClosesAnAbsentFindingWithoutErasingItsEarlierActiveRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var nodes = new[] { GraphRevisionTestBuilder.Node("method:left", "hash:left"), GraphRevisionTestBuilder.Node("method:right", "hash:right") };
        var firstRevision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, nodes);
        var findings = new DuplicateFindingRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var observation = await findings.RecordObservationAsync(initialized.Value.StateLocation, new DuplicateFindingObservationFact(
            new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:left"), new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "method:right"), firstRevision, "aggregate-v1", .9d, "{}", [new DuplicateSignalFact(DuplicateSignalKind.Structural, .9d, "{}")]), CancellationToken.None);
        Assert.True(observation.IsSuccess);
        var lifecycle = new DuplicateFindingLifecycleRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var opened = await lifecycle.ReconcileAsync(initialized.Value.StateLocation, firstRevision, [observation.Value.FindingId], new Dictionary<string, string>(), CancellationToken.None);
        var secondRevision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, nodes);
        var closed = await lifecycle.ReconcileAsync(initialized.Value.StateLocation, secondRevision, [], new Dictionary<string, string>(), CancellationToken.None);
        var activeFirst = await lifecycle.ListActiveAtAsync(initialized.Value.StateLocation, firstRevision, CancellationToken.None);
        var activeSecond = await lifecycle.ListActiveAtAsync(initialized.Value.StateLocation, secondRevision, CancellationToken.None);

        Assert.Equal(DuplicateFindingLifecycleState.Active, Assert.Single(opened.Value).State);
        Assert.Equal(DuplicateFindingLifecycleState.Closed, Assert.Single(closed.Value).State);
        Assert.Single(activeFirst.Value);
        Assert.Empty(activeSecond.Value);
    }
}
