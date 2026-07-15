using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceExecutionTests
{
    [Fact]
    public async Task AnalyzeBuildsAnInitialCSharpGraphRevisionAndThenSkipsAnUnchangedWorkspace()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Order.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            using System;
            namespace Sample;
            public sealed class Order : IEquatable<Order>
            {
                public string Id { get; init; } = string.Empty;
            }
            """);
        var handler = AnalyzeWorkspaceTestSupport.CreateHandler();

        var initial = await handler.Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(initial.IsSuccess);
        Assert.True(initial.Value.IsComplete);
        Assert.False(initial.Value.WasNoOp);
        Assert.NotNull(initial.Value.RunId);
        Assert.NotNull(initial.Value.GraphRevision);
        Assert.Equal(1L, initial.Value.GraphRevision.Revision);
        Assert.Equal(3, initial.Value.GraphRevision.NodeCount);
        Assert.Equal(2, initial.Value.GraphRevision.EdgeCount);
        Assert.Equal(1, initial.Value.GraphRevision.SymbolCount);
        Assert.Equal(1, initial.Value.GraphRevision.InterfaceFingerprintCount);
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            initial.Value.RunId!,
            expectedStatus: "succeeded",
            expectedGraphRevision: 1,
            expectedEvents: ["started", "succeeded"]);
        await SqliteAssertions.AssertGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            revision: 1,
            initial.Value.RunId!,
            expectedNodeCount: 3,
            expectedEdgeCount: 2);

        var unchanged = await handler.Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(unchanged.IsSuccess);
        Assert.True(unchanged.Value.IsComplete);
        Assert.True(unchanged.Value.WasNoOp);
        Assert.Null(unchanged.Value.RunId);
        Assert.Null(unchanged.Value.GraphRevision);
        Assert.All(
            unchanged.Value.Inventory.Changes,
            static change => Assert.Equal(Archy.Features.Analysis.InventorySources.SourceFileChangeKind.Unchanged, change.Kind));
        Assert.Equal(1L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "analysis_runs"));
        Assert.Equal(1L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));
    }
}
