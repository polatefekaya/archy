using Archy.Features.Memory.Summaries;
using Archy.Features.Memory.SummaryBatches;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Memory.SummaryBatches;

public sealed class SummaryBatchRepositoryTests
{
    [Fact]
    public async Task CreateRoundTripsOrderedMembersCoTouchEvidenceAndSummaryVersionLinks()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("type:sample.first", "hash:first")]);
        var sessions = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var session = await sessions.StartAsync(
            initialized.Value.StateLocation,
            new SessionStartFact("session:summary-batch", "codex", "external:summary-batch", "user", "tester", "{}"),
            CancellationToken.None);
        Assert.True(session.IsSuccess);
        var batches = new SummaryBatchRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var fact = new SummaryBatchFact(
            "summary-batch:1",
            session.Value.SessionId,
            "session_idle",
            SummaryBatchRequestState.Requested,
            revision,
            "{\"model\":\"gpt-5\"}",
            [
                new SummaryBatchMemberFact("type", "type:sample.first", 3, [2]),
                new SummaryBatchMemberFact("type", "type:sample.second", 7, [1]),
            ]);

        var created = await batches.CreateAsync(initialized.Value.StateLocation, fact, CancellationToken.None);
        var read = await batches.GetAsync(initialized.Value.StateLocation, fact.SummaryBatchId, CancellationToken.None);

        Assert.True(created.IsSuccess, created.IsSuccess ? string.Empty : created.Problem!.Message);
        Assert.True(read.IsSuccess, read.IsSuccess ? string.Empty : read.Problem!.Message);
        Assert.Equal(created.Value.SummaryBatchId, read.Value.SummaryBatchId);
        Assert.Equal(created.Value.SessionId, read.Value.SessionId);
        Assert.Equal(created.Value.SourceGraphRevision, read.Value.SourceGraphRevision);
        Assert.Collection(
            read.Value.Members,
            first =>
            {
                Assert.Equal(1, first.MemberOrdinal);
                Assert.Equal([2], first.CoTouchedMemberOrdinals);
            },
            second =>
            {
                Assert.Equal(2, second.MemberOrdinal);
                Assert.Equal([1], second.CoTouchedMemberOrdinals);
            });

        var summary = await new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).AppendAsync(
            initialized.Value.StateLocation,
            new SummaryVersionFact(
                "summary:type:sample.first",
                "type",
                "type:sample.first",
                revision,
                "deadbeef",
                "Summary",
                "Changed summary",
                "openai",
                "gpt-5",
                "{}",
                SummaryStaleness.Fresh,
                fact.SummaryBatchId),
            CancellationToken.None);

        Assert.True(summary.IsSuccess, summary.IsSuccess ? string.Empty : summary.Problem!.Message);
        Assert.Equal(fact.SummaryBatchId, summary.Value.OriginatingSummaryBatchId);
        Assert.Equal(2L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "summary_batch_members"));
    }

    [Fact]
    public async Task CreateRejectsAnAsymmetricCoTouchGraphBeforeWritingAnything()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var repository = new SummaryBatchRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var invalid = new SummaryBatchFact(
            "summary-batch:invalid",
            "missing-session",
            "session_idle",
            SummaryBatchRequestState.Pending,
            1,
            "{}",
            [
                new SummaryBatchMemberFact("type", "type:first", 1, [2]),
                new SummaryBatchMemberFact("type", "type:second", 2, []),
            ]);

        var result = await repository.CreateAsync(initialized.Value.StateLocation, invalid, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.Equal(0L, await SqliteAssertions.CountAsync(initialized.Value.StateLocation.DatabasePath, "summary_batches"));
    }
}
