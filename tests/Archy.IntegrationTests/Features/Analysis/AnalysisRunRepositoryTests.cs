using Microsoft.Data.Sqlite;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis;

public sealed class AnalysisRunRepositoryTests
{
    [Fact]
    public async Task StartPersistsImmutableRepositoryProvenanceAlongsideTheAnalysisRun()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var provenance = new RepositoryProvenance(
            "0123456789abcdef0123456789abcdef01234567",
            RepositoryWorktreeState.Dirty,
            ["src/Changed.cs", "src/New.cs"]);

        var started = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            initialized.Value.StateLocation,
            "archy-test",
            "configuration-hash",
            provenance,
            CancellationToken.None);

        Assert.True(started.IsSuccess);
        Assert.Equal(provenance, started.Value.RepositoryProvenance);
        Assert.Equal(1L, await SqliteAssertions.CountAsync(
            initialized.Value.StateLocation.DatabasePath,
            "analysis_run_repository_provenance"));
        await using var connection = new SqliteConnection($"Data Source={initialized.Value.StateLocation.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT head_commit, worktree_state, changed_paths_json FROM analysis_run_repository_provenance WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", started.Value.RunId);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(provenance.HeadCommit, row.GetString(0));
        Assert.Equal("dirty", row.GetString(1));
        Assert.Equal("[\"src/Changed.cs\",\"src/New.cs\"]", row.GetString(2));
    }

    [Fact]
    public async Task CompleteRecordsNonSuccessRunsAppendOnlyAndWithoutGraphRevisions()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var store = new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
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
