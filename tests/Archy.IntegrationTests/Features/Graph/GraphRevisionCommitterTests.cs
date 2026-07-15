using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph;

public sealed class GraphRevisionCommitterTests
{
    [Fact]
    public async Task CommitPersistsACompleteImmutableGraphRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var run = await StartRunAsync(initialized.Value.StateLocation);
        var nodes = new[]
        {
            new GraphNodeFact("file:program", "file", "Program.cs", "Program.cs", "Program.cs", 1, 10, "fixture", 1, "{\"kind\":\"file\"}", "hash:file:program:v1"),
            new GraphNodeFact("type:program", "type", "Archy.Program", "Program", "Program.cs", 3, 10, "fixture", 0.95, "{\"kind\":\"symbol\"}", "hash:type:program:v1"),
        };
        var edge = new GraphEdgeFact(
            "contains:file:program:type:program",
            "file:program",
            "type:program",
            "contains",
            "program.cs#type:archy.program",
            "fixture",
            1,
            "{\"source\":\"Program.cs:3\"}");

        var store = new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var committed = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            nodes,
            [edge],
            CancellationToken.None);

        Assert.True(committed.IsSuccess);
        Assert.Equal(1L, committed.Value.Revision);
        Assert.Equal(2, committed.Value.NodeCount);
        Assert.Equal(1, committed.Value.EdgeCount);

        var duplicateCommit = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            nodes,
            [edge],
            CancellationToken.None);
        Assert.False(duplicateCommit.IsSuccess);
        Assert.Equal("conflict", duplicateCommit.Problem!.Code);

        await SqliteAssertions.AssertGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            committed.Value.Revision,
            run.RunId,
            expectedNodeCount: 2,
            expectedEdgeCount: 1);
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            committed.Value.Revision);
        await SqliteAssertions.AssertGraphEdgeAsync(
            initialized.Value.StateLocation.DatabasePath,
            committed.Value.Revision,
            edge.EdgeId,
            expectedJoinKey: edge.NormalizedJoinKey!,
            expectedEvidenceJson: edge.EvidenceJson);
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            run.RunId,
            expectedStatus: "succeeded",
            expectedGraphRevision: committed.Value.Revision,
            expectedEvents: ["started", "succeeded"]);
    }

    [Fact]
    public async Task CommitRejectsInvalidFactsWithoutExposingAPartialRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var run = await StartRunAsync(initialized.Value.StateLocation);
        var node = new GraphNodeFact("file:program", "file", "Program.cs", "Program.cs", "Program.cs", 1, 1, "fixture", 1, "{}", "hash:file:program:v1");
        var invalidEdge = new GraphEdgeFact("invalid", "file:program", "missing:node", "calls", null, "fixture", 1, "{}");
        var store = new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var rejected = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [node],
            [invalidEdge],
            CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation", rejected.Problem!.Code);
        Assert.Equal(
            0L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            run.RunId,
            expectedStatus: "running",
            expectedGraphRevision: null,
            expectedEvents: ["started"]);

        var recovered = await store.CommitAsync(
            initialized.Value.StateLocation,
            run.RunId,
            [node],
            [],
            CancellationToken.None);
        Assert.True(recovered.IsSuccess);
    }

    [Fact]
    public async Task CommitMaintainsNodeAndEdgeValidityRangesAcrossChangesAndDeletion()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var store = new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var file = CreateNode("file:program", "file", "Program.cs", "hash:file:v1");
        var originalType = CreateNode("type:program", "type", "Archy.Program", "hash:type:v1");
        var changedType = CreateNode("type:program", "type", "Archy.Program", "hash:type:v2");
        var edge = CreateContainsEdge();

        var firstRun = await StartRunAsync(initialized.Value.StateLocation);
        var first = await store.CommitAsync(
            initialized.Value.StateLocation,
            firstRun.RunId,
            [file, originalType],
            [edge],
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondRun = await StartRunAsync(initialized.Value.StateLocation);
        var second = await store.CommitAsync(
            initialized.Value.StateLocation,
            secondRun.RunId,
            [file, changedType],
            [],
            CancellationToken.None);
        Assert.True(second.IsSuccess);

        var thirdRun = await StartRunAsync(initialized.Value.StateLocation);
        var third = await store.CommitAsync(
            initialized.Value.StateLocation,
            thirdRun.RunId,
            [file],
            [],
            CancellationToken.None);
        Assert.True(third.IsSuccess);

        var fileVersions = await GraphVersionReader.ReadNodeVersionsAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            file.StableId);
        Assert.Collection(
            fileVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Null(version.ValidToRevision);
                Assert.Equal("hash:file:v1", version.ContentHash);
            });

        var typeVersions = await GraphVersionReader.ReadNodeVersionsAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            originalType.StableId);
        Assert.Collection(
            typeVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Equal(1L, version.ValidToRevision);
                Assert.Equal("hash:type:v1", version.ContentHash);
            },
            version =>
            {
                Assert.Equal(2L, version.ValidFromRevision);
                Assert.Equal(2L, version.ValidToRevision);
                Assert.Equal("hash:type:v2", version.ContentHash);
            });

        var edgeVersions = await GraphVersionReader.ReadEdgeVersionsAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            edge.EdgeId);
        Assert.Collection(
            edgeVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Equal(1L, version.ValidToRevision);
            });
    }

    [Fact]
    public async Task CommitRejectsAnEdgeIdentityMutationWithoutExposingTheRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var store = new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var file = CreateNode("file:program", "file", "Program.cs", "hash:file:v1");
        var originalType = CreateNode("type:program", "type", "Archy.Program", "hash:type:v1");
        var replacementType = CreateNode("type:replacement", "type", "Archy.Replacement", "hash:type:replacement:v1");
        var edge = CreateContainsEdge();

        var firstRun = await StartRunAsync(initialized.Value.StateLocation);
        var first = await store.CommitAsync(
            initialized.Value.StateLocation,
            firstRun.RunId,
            [file, originalType],
            [edge],
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var secondRun = await StartRunAsync(initialized.Value.StateLocation);
        var mutatedEdge = edge with { TargetStableId = replacementType.StableId };
        var rejected = await store.CommitAsync(
            initialized.Value.StateLocation,
            secondRun.RunId,
            [file, replacementType],
            [mutatedEdge],
            CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);
        Assert.Equal(
            1L,
            await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "graph_revisions"));
        await SqliteAssertions.AssertActiveGraphRevisionAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            first.Value.Revision);
        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            secondRun.RunId,
            expectedStatus: "running",
            expectedGraphRevision: null,
            expectedEvents: ["started"]);
    }

    [Fact]
    public async Task CommitVersionsSymbolsAndInterfaceFingerprintsWithTheGraphRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var store = new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var methodNode = CreateNode("method:program.run", "method", "Archy.Program.Run", "hash:method:v1");
        var firstSymbol = CreateSymbol("hash:signature:v1");
        var secondSymbol = CreateSymbol("hash:signature:v2");
        var firstFingerprint = CreateFingerprint("hash:interface:v1");
        var secondFingerprint = CreateFingerprint("hash:interface:v2");

        var firstRun = await StartRunAsync(initialized.Value.StateLocation);
        var first = await store.CommitAsync(
            initialized.Value.StateLocation,
            firstRun.RunId,
            [methodNode],
            [],
            [firstSymbol],
            [firstFingerprint],
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value.SymbolCount);
        Assert.Equal(1, first.Value.InterfaceFingerprintCount);

        var secondRun = await StartRunAsync(initialized.Value.StateLocation);
        var second = await store.CommitAsync(
            initialized.Value.StateLocation,
            secondRun.RunId,
            [methodNode],
            [],
            [secondSymbol],
            [secondFingerprint],
            CancellationToken.None);
        Assert.True(second.IsSuccess);

        var thirdRun = await StartRunAsync(initialized.Value.StateLocation);
        var third = await store.CommitAsync(
            initialized.Value.StateLocation,
            thirdRun.RunId,
            [methodNode],
            [],
            [],
            [],
            CancellationToken.None);
        Assert.True(third.IsSuccess);

        var symbolVersions = await GraphVersionReader.ReadSymbolVersionsAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            firstSymbol.SymbolId);
        Assert.Collection(
            symbolVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Equal(1L, version.ValidToRevision);
                Assert.Equal("hash:signature:v1", version.SignatureHash);
            },
            version =>
            {
                Assert.Equal(2L, version.ValidFromRevision);
                Assert.Equal(2L, version.ValidToRevision);
                Assert.Equal("hash:signature:v2", version.SignatureHash);
            });

        var fingerprintVersions = await GraphVersionReader.ReadInterfaceFingerprintVersionsAsync(
            initialized.Value.StateLocation.DatabasePath,
            initialized.Value.StateLocation.WorkspaceId,
            firstFingerprint.SymbolId,
            firstFingerprint.FingerprintKind);
        Assert.Collection(
            fingerprintVersions,
            version =>
            {
                Assert.Equal(1L, version.ValidFromRevision);
                Assert.Equal(1L, version.ValidToRevision);
                Assert.Equal("hash:interface:v1", version.FingerprintHash);
            },
            version =>
            {
                Assert.Equal(2L, version.ValidFromRevision);
                Assert.Equal(2L, version.ValidToRevision);
                Assert.Equal("hash:interface:v2", version.FingerprintHash);
            });
    }

    private static GraphNodeFact CreateNode(string stableId, string nodeKind, string canonicalKey, string contentHash) => new(
        stableId,
        nodeKind,
        canonicalKey,
        canonicalKey,
        "Program.cs",
        1,
        10,
        "fixture",
        1,
        "{}",
        contentHash);

    private static GraphEdgeFact CreateContainsEdge() => new(
        "contains:file:program:type:program",
        "file:program",
        "type:program",
        "contains",
        "program.cs#type:archy.program",
        "fixture",
        1,
        "{}");

    private static GraphSymbolFact CreateSymbol(string signatureHash) => new(
        "symbol:archy.program.run",
        "method:program.run",
        "Archy.Program.Run",
        "public",
        "void Archy.Program.Run()",
        "[]",
        "{\"type\":\"void\"}",
        signatureHash);

    private static InterfaceFingerprintFact CreateFingerprint(string fingerprintHash) => new(
        "symbol:archy.program.run",
        "public_surface",
        fingerprintHash,
        "[\"void Run()\"]");

    private static async Task<AnalysisRun> StartRunAsync(
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location)
    {
        var started = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: null,
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        return started.Value;
    }
}
