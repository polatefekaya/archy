using Archy.Features.Storage.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Storage;

public sealed class AnalysisRunStoreTests
{
    [Fact]
    public async Task CompleteRecordsNonSuccessRunsAppendOnlyAndWithoutGraphRevisions()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var store = new AnalysisRunStore(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var started = await store.StartAsync(
            initialized.Value.StateLocation,
            "archy-test",
            "configuration-hash",
            repositoryCommit: "deadbeef",
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        Assert.Equal(AnalysisRunStatus.Running, started.Value.Status);

        var directSuccess = await store.CompleteAsync(
            initialized.Value.StateLocation,
            started.Value.RunId,
            AnalysisRunStatus.Succeeded,
            graphRevision: null,
            CancellationToken.None);
        Assert.False(directSuccess.IsSuccess);
        Assert.Equal("validation", directSuccess.Problem!.Code);

        var failedWithGraphRevision = await store.CompleteAsync(
            initialized.Value.StateLocation,
            started.Value.RunId,
            AnalysisRunStatus.Failed,
            graphRevision: 1,
            CancellationToken.None);
        Assert.False(failedWithGraphRevision.IsSuccess);
        Assert.Equal("validation", failedWithGraphRevision.Problem!.Code);

        var completed = await store.CompleteAsync(
            initialized.Value.StateLocation,
            started.Value.RunId,
            AnalysisRunStatus.Failed,
            graphRevision: null,
            CancellationToken.None);
        Assert.True(completed.IsSuccess);
        Assert.Equal(AnalysisRunStatus.Failed, completed.Value.Status);
        Assert.Equal(started.Value.AnalyzerVersion, completed.Value.AnalyzerVersion);

        var duplicateCompletion = await store.CompleteAsync(
            initialized.Value.StateLocation,
            started.Value.RunId,
            AnalysisRunStatus.Cancelled,
            graphRevision: null,
            CancellationToken.None);
        Assert.False(duplicateCompletion.IsSuccess);
        Assert.Equal("conflict", duplicateCompletion.Problem!.Code);

        await SqliteAssertions.AssertAnalysisRunAsync(
            initialized.Value.StateLocation.DatabasePath,
            started.Value.RunId,
            expectedStatus: "failed",
            expectedGraphRevision: null,
            expectedEvents: ["started", "failed"]);
    }
}
