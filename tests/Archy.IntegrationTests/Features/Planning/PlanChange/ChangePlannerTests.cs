using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.PlanChange;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Planning.PlanChange;

public sealed class ChangePlannerTests
{
    [Fact]
    public async Task ProducesBoundedEvidenceBasedPlanAndRejectsPathTraversal()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var target = GraphRevisionTestBuilder.Node("target", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions.cs" };
        var candidate = GraphRevisionTestBuilder.Node("candidate", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions.cs" };
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [target, candidate]);
        var locks = new WorkspaceLockManager(TimeProvider.System); var decisions = new ArchitectureDecisionRepository(TimeProvider.System, locks); var recorded = await decisions.RecordAsync(initialized.Value.StateLocation, new("reuse", DecisionResolution.Accepted, "existing session behavior", "agent", "fixture", null, revision, [new(ArchitectureTargetKind.GraphNode, target.StableId)]), CancellationToken.None); Assert.True(recorded.IsSuccess);
        var planner = new ChangePlanner(new GraphRevisionSnapshotReader(locks), new ImpactAnalyzer(new GraphTraversalReader(locks), new GraphRevisionSnapshotReader(locks)));

        var plan = await planner.PlanAsync(initialized.Value.StateLocation, new("create session", ["src/Sessions.cs"], target.StableId), CancellationToken.None);
        var invalid = await planner.PlanAsync(initialized.Value.StateLocation, new("create session", ["../escape.cs"]), CancellationToken.None);

        Assert.True(plan.IsSuccess); Assert.Equal(1, plan.Value!.GraphRevision); Assert.NotEmpty(plan.Value.Candidates); Assert.Equal(["src/Sessions.cs"], plan.Value.ExistingFiles); Assert.Equal(recorded.Value!.DecisionId, Assert.Single(plan.Value.DecisionIds)); Assert.False(invalid.IsSuccess);
    }
}
