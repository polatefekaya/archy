using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph;

public sealed class GraphFactValidationTests
{
    [Theory]
    [MemberData(nameof(InvalidNodeFacts))]
    public async Task CommitRejectsInvalidNodeFactsWithoutCreatingAGraphRevision(GraphNodeFact? node)
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var run = await StartRunAsync(initialized.Value.StateLocation);

        var result = await CreateStore().CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [node!],
            [],
            CancellationToken.None);

        await AssertRejectedWithoutRevisionAsync(initialized.Value.StateLocation, run.RunId, result);
    }

    [Theory]
    [MemberData(nameof(InvalidEdgeFacts))]
    public async Task CommitRejectsInvalidEdgeFactsWithoutCreatingAGraphRevision(GraphEdgeFact? edge)
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var run = await StartRunAsync(initialized.Value.StateLocation);

        var result = await CreateStore().CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [CreateNode()],
            [edge!],
            CancellationToken.None);

        await AssertRejectedWithoutRevisionAsync(initialized.Value.StateLocation, run.RunId, result);
    }

    [Theory]
    [MemberData(nameof(InvalidSymbolFacts))]
    public async Task CommitRejectsMalformedSymbolMetadataWithoutCreatingAGraphRevision(GraphSymbolFact symbol)
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var run = await StartRunAsync(initialized.Value.StateLocation);

        var result = await CreateStore().CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [CreateNode()],
            [],
            [symbol],
            [],
            CancellationToken.None);

        await AssertRejectedWithoutRevisionAsync(initialized.Value.StateLocation, run.RunId, result);
    }

    [Fact]
    public async Task CommitRejectsMalformedInterfaceFingerprintMetadataWithoutCreatingAGraphRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var run = await StartRunAsync(initialized.Value.StateLocation);
        var symbol = CreateSymbol();

        var result = await CreateStore().CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [CreateNode()],
            [],
            [symbol],
            [new InterfaceFingerprintFact(symbol.SymbolId, "public_contract", "hash:fingerprint", "not-json")],
            CancellationToken.None);

        await AssertRejectedWithoutRevisionAsync(initialized.Value.StateLocation, run.RunId, result);
    }

    public static IEnumerable<object?[]> InvalidNodeFacts()
    {
        yield return [null];
        yield return [CreateNode() with { Confidence = double.NaN }];
        yield return [CreateNode() with { EvidenceJson = "not-json" }];
        yield return [CreateNode() with { StartLine = 10, EndLine = 1 }];
        yield return [CreateNode() with { StartLine = 1, EndLine = null }];
    }

    public static IEnumerable<object?[]> InvalidEdgeFacts()
    {
        yield return [null];
        yield return [CreateEdge() with { Confidence = double.PositiveInfinity }];
        yield return [CreateEdge() with { EvidenceJson = "not-json" }];
    }

    public static IEnumerable<object[]> InvalidSymbolFacts()
    {
        yield return [CreateSymbol() with { ParameterMetadataJson = "not-json" }];
        yield return [CreateSymbol() with { ReturnMetadataJson = "not-json" }];
    }

    private static GraphRevisionCommitter CreateStore() =>
        new(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

    private static GraphNodeFact CreateNode() => new(
        "method:program.run",
        "method",
        "Archy.Program.Run",
        "Run",
        "Program.cs",
        1,
        10,
        "fixture",
        1,
        "{}",
        "hash:method:program.run");

    private static GraphEdgeFact CreateEdge() => new(
        "calls:program.run:dependency",
        "method:program.run",
        "method:program.run",
        "calls",
        null,
        "fixture",
        1,
        "{}");

    private static GraphSymbolFact CreateSymbol() => new(
        "symbol:archy.program.run",
        "method:program.run",
        "Archy.Program.Run()",
        "public",
        "Run()",
        "[]",
        "{\"type\":\"void\"}",
        "hash:symbol:program.run");

    private static async Task<AnalysisRun> StartRunAsync(WorkspaceStateLocation location)
    {
        var result = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: "deadbeef",
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task AssertRejectedWithoutRevisionAsync(
        WorkspaceStateLocation location,
        string runId,
        Archy.SharedKernel.Primitives.Result<CommittedGraphRevision> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.Equal(0L, await SqliteAssertions.CountAsync(location.DatabasePath, "graph_revisions"));
        await SqliteAssertions.AssertAnalysisRunAsync(
            location.DatabasePath,
            runId,
            expectedStatus: "running",
            expectedGraphRevision: null,
            expectedEvents: ["started"]);
    }
}
