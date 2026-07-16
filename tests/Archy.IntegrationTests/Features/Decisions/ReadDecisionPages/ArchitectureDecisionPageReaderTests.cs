using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Decisions.ReadDecisionPages;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Decisions.ReadDecisionPages;

public sealed class ArchitectureDecisionPageReaderTests
{
    [Fact]
    public async Task PagesDecisionIdentitiesWhileRetainingEveryTargetForTheSelectedDecision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var left = GraphRevisionTestBuilder.Node("node:left", "left");
        var right = GraphRevisionTestBuilder.Node("node:right", "right");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [left, right]);
        var repository = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        for (var index = 0; index < 2; index++)
        {
            var recorded = await repository.RecordAsync(initialized.Value.StateLocation, new ArchitectureDecisionFact("duplicate_review", DecisionResolution.Accepted, $"note {index}", "agent", "fixture", null, revision, [new ArchitectureTarget(ArchitectureTargetKind.GraphNode, left.StableId), new ArchitectureTarget(ArchitectureTargetKind.GraphNode, right.StableId)]), CancellationToken.None);
            Assert.True(recorded.IsSuccess);
        }

        var page = await new ArchitectureDecisionPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation, new ArchitectureTarget(ArchitectureTargetKind.GraphNode, left.StableId), 0, 1, CancellationToken.None);

        Assert.True(page.IsSuccess, page.IsSuccess ? string.Empty : page.Problem!.Message);
        Assert.Equal(2, page.Value.TotalCount);
        Assert.Equal(2, Assert.Single(page.Value.Items).Targets.Count);
    }

    [Fact]
    public async Task RejectsOversizedDecisionPageBeforeReadingStorage()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var result = await new ArchitectureDecisionPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation, new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "node"), 0, 101, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }
}
