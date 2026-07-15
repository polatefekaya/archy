using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Memory.Summaries;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Memory;

public sealed class SummaryRepositoryTests
{
    [Fact]
    public async Task AppendPreservesChronologicalSummaryVersionsAndSupersession()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var firstRevision = await CommitSourceRevisionAsync(initialized.Value.StateLocation, "hash:node:v1");
        var secondRevision = await CommitSourceRevisionAsync(initialized.Value.StateLocation, "hash:node:v2");
        var store = new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var first = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(firstRevision, "First summary", "Initial English diff", SummaryStaleness.Fresh),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value.VersionNumber);
        Assert.Null(first.Value.SupersedesSummaryVersionId);

        var second = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(secondRevision, "Second summary", "Updated English diff", SummaryStaleness.Stale),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.Value.VersionNumber);
        Assert.Equal(first.Value.SummaryVersionId, second.Value.SupersedesSummaryVersionId);

        var listed = await store.ListAsync(
            initialized.Value.StateLocation,
            "summary:type:archy.program",
            CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Collection(
            listed.Value,
            version =>
            {
                Assert.Equal(1, version.VersionNumber);
                Assert.Equal(firstRevision, version.SourceGraphRevision);
                Assert.Equal("First summary", version.SummaryText);
                Assert.Equal(SummaryStaleness.Fresh, version.Staleness);
            },
            version =>
            {
                Assert.Equal(2, version.VersionNumber);
                Assert.Equal(secondRevision, version.SourceGraphRevision);
                Assert.Equal("Second summary", version.SummaryText);
                Assert.Equal(first.Value.SummaryVersionId, version.SupersedesSummaryVersionId);
                Assert.Equal(SummaryStaleness.Stale, version.Staleness);
            });
    }

    [Fact]
    public async Task AppendRejectsASummaryWithoutAnInWorkspaceSourceRevision()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var result = await new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(sourceGraphRevision: 999, "Untrusted summary", "No source revision", SummaryStaleness.Fresh),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Problem!.Code);
    }

    [Fact]
    public async Task AppendRejectsMalformedProviderMetadataWithoutCreatingSummaryIdentity()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitSourceRevisionAsync(initialized.Value.StateLocation, "hash:node:v1");
        var store = new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var result = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(revision, "Summary", "Diff", SummaryStaleness.Fresh) with { ProviderMetadataJson = "not-json" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        var summaries = await store.ListAsync(initialized.Value.StateLocation, "summary:type:archy.program", CancellationToken.None);
        Assert.True(summaries.IsSuccess);
        Assert.Empty(summaries.Value);
    }

    [Fact]
    public async Task AppendRejectsAnUndefinedStalenessValueWithoutCreatingSummaryIdentity()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitSourceRevisionAsync(initialized.Value.StateLocation, "hash:node:v1");
        var store = new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var result = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(revision, "Summary", "Diff", (SummaryStaleness)999),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.Equal(0L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "summary_identities"));
    }

    [Fact]
    public async Task AppendRejectsAnAttemptToRetargetAnExistingSummary()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitSourceRevisionAsync(initialized.Value.StateLocation, "hash:node:v1");
        var store = new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var first = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(revision, "Original summary", "Original diff", SummaryStaleness.Fresh),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var retargeted = await store.AppendAsync(
            initialized.Value.StateLocation,
            CreateFact(revision, "Retargeted summary", "Retargeted diff", SummaryStaleness.Fresh) with
            {
                TargetStableId = "type:another.program",
            },
            CancellationToken.None);

        Assert.False(retargeted.IsSuccess);
        Assert.Equal("conflict", retargeted.Problem!.Code);
        var summaries = await store.ListAsync(initialized.Value.StateLocation, "summary:type:archy.program", CancellationToken.None);
        Assert.True(summaries.IsSuccess);
        var onlyVersion = Assert.Single(summaries.Value);
        Assert.Equal("Original summary", onlyVersion.SummaryText);
    }

    private static SummaryVersionFact CreateFact(
        long sourceGraphRevision,
        string summaryText,
        string englishDiff,
        SummaryStaleness staleness) => new(
        "summary:type:archy.program",
        "type",
        "type:archy.program",
        sourceGraphRevision,
        "deadbeef",
        summaryText,
        englishDiff,
        "openai",
        "gpt-5",
        "{\"request_id\":\"fixture\"}",
        staleness);

    private static async Task<long> CommitSourceRevisionAsync(
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        string contentHash)
    {
        var run = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: "deadbeef",
            CancellationToken.None);
        Assert.True(run.IsSuccess);

        var committed = await new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).CommitAsync(
            location,
            run.Value.RunId,
            [new GraphNodeFact("type:archy.program", "type", "Archy.Program", "Program", "Program.cs", 1, 10, "fixture", 1, "{}", contentHash)],
            [],
            CancellationToken.None);
        Assert.True(committed.IsSuccess);
        return committed.Value.Revision;
    }
}
