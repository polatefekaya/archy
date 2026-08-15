using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.PlanSafeRefactor;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Planning.PlanSafeRefactor;

public sealed class SafeRefactorPlannerTests
{
    [Fact]
    public async Task ProducesSequencedCheckpointsAndPublicCompatibilityGuidance()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var target = GraphRevisionTestBuilder.Node("node:public-target", "aaaaaaaaaaaaaaaa");
        var caller = GraphRevisionTestBuilder.Node("node:caller", "bbbbbbbbbbbbbbbb");
        var symbol = new GraphSymbolFact("symbol:public-target", target.StableId, "Sample.Target", "public", "Sample.Target()", "[]", "{}", "aaaaaaaaaaaaaaaa");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [target, caller], [GraphRevisionTestBuilder.Edge("edge:caller-target", caller.StableId, target.StableId)], [symbol]);
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var planner = new SafeRefactorPlanner(new GraphRevisionSnapshotReader(locks), new ImpactAnalyzer(new GraphTraversalReader(locks), new GraphRevisionSnapshotReader(locks)));

        var result = await planner.PlanAsync(initialized.Value.StateLocation, new(target.StableId, RefactorIntent.Move, "src/NewTarget.cs"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal(["baseline", "isolate", "migrate callers", "preserve compatibility", "remove old path", "verify"], result.Value!.Checkpoints.Select(checkpoint => checkpoint.Phase));
        Assert.NotEmpty(result.Value.CompatibilityGuidance); Assert.Contains(result.Value.Checkpoints, checkpoint => checkpoint.ArchyCheck == "find_reintroduced");
    }
}
