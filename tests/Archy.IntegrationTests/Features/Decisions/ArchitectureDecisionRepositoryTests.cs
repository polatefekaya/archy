using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Decisions;

public sealed class ArchitectureDecisionRepositoryTests
{
    [Fact]
    public async Task RecordRejectsUndefinedResolutionAndTargetKindsWithoutCreatingADecision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var store = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var fact = new ArchitectureDecisionFact(
            "rule_review",
            DecisionResolution.Accepted,
            null,
            "human",
            "fixture-user",
            null,
            null,
            [new ArchitectureTarget(ArchitectureTargetKind.Rule, "rule:fixture")]);

        var undefinedResolution = await store.RecordAsync(
            initialized.Value.StateLocation,
            fact with { Resolution = (DecisionResolution)999 },
            CancellationToken.None);
        var undefinedTarget = await store.RecordAsync(
            initialized.Value.StateLocation,
            fact with { Targets = [new ArchitectureTarget((ArchitectureTargetKind)999, "unknown:target")] },
            CancellationToken.None);

        Assert.False(undefinedResolution.IsSuccess);
        Assert.Equal("validation", undefinedResolution.Problem!.Code);
        Assert.False(undefinedTarget.IsSuccess);
        Assert.Equal("validation", undefinedTarget.Problem!.Code);
        var decisions = await store.ListForTargetAsync(
            initialized.Value.StateLocation,
            fact.Targets[0],
            CancellationToken.None);
        Assert.True(decisions.IsSuccess);
        Assert.Empty(decisions.Value);
    }

    [Fact]
    public async Task RecordAcceptsExistingGraphEdgeAndSymbolTargets()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("method:archy.program.run", "hash:method:v1");
        var edge = GraphRevisionTestBuilder.Edge("calls:archy.program.run:dependency", node.StableId, node.StableId);
        var symbol = new GraphSymbolFact(
            "symbol:archy.program.run",
            node.StableId,
            "Archy.Program.Run()",
            "public",
            "Run()",
            "[]",
            "{\"type\":\"void\"}",
            "hash:symbol:v1");
        var revision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [node],
            [edge],
            [symbol]);
        var edgeTarget = new ArchitectureTarget(ArchitectureTargetKind.GraphEdge, edge.EdgeId);
        var symbolTarget = new ArchitectureTarget(ArchitectureTargetKind.GraphSymbol, symbol.SymbolId);
        var store = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var recorded = await store.RecordAsync(
            initialized.Value.StateLocation,
            new ArchitectureDecisionFact(
                "public_contract_review",
                DecisionResolution.Accepted,
                "The graph evidence supports this contract change.",
                "human",
                "fixture-user",
                null,
                revision,
                [edgeTarget, symbolTarget]),
            CancellationToken.None);

        Assert.True(recorded.IsSuccess);
        Assert.Equal([edgeTarget, symbolTarget], recorded.Value.Targets);
        var decisionsForSymbol = await store.ListForTargetAsync(
            initialized.Value.StateLocation,
            symbolTarget,
            CancellationToken.None);
        Assert.True(decisionsForSymbol.IsSuccess);
        var decision = Assert.Single(decisionsForSymbol.Value);
        Assert.Equal(recorded.Value.DecisionId, decision.DecisionId);
        Assert.Equal([edgeTarget, symbolTarget], decision.Targets);
    }
}
