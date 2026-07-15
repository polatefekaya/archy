using Archy.Features.Analysis.WatchWorkspaceChanges;

namespace Archy.UnitTests.Features.Analysis.WatchWorkspaceChanges;

public sealed class WorkspaceChangeDebounceControllerTests
{
    [Fact]
    public async Task CoalescesAtomicSaveNoiseAndWaitsForTheSessionIdleWindow()
    {
        var dispatched = new TaskCompletionSource<WorkspaceChangeSet>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new WorkspaceChangeDebounceController(
            TimeProvider.System,
            new WorkspaceWatchOptions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60)),
            (changeSet, _) =>
            {
                dispatched.TrySetResult(changeSet);
                return ValueTask.CompletedTask;
            });

        controller.RecordPath("src/Feature.cs");
        controller.RecordPath("src/Feature.cs");
        controller.RecordSessionActivity();
        await Task.Delay(35);
        Assert.False(dispatched.Task.IsCompleted);

        var settled = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["src/Feature.cs"], settled.RepositoryRelativePaths);
        Assert.False(settled.RequiresFullInventoryReconciliation);
    }

    [Fact]
    public async Task RequeuesAFailedDispatchBeforeRunningTheFinalSettledContent()
    {
        var attempts = 0;
        var completed = new TaskCompletionSource<WorkspaceChangeSet>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new WorkspaceChangeDebounceController(
            TimeProvider.System,
            new WorkspaceWatchOptions(TimeSpan.FromMilliseconds(15), TimeSpan.Zero),
            (changeSet, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("transient");
                }

                completed.TrySetResult(changeSet);
                return ValueTask.CompletedTask;
            });

        controller.RecordPath("src/Retry.cs");
        var settled = await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, attempts);
        Assert.Equal(["src/Retry.cs"], settled.RepositoryRelativePaths);
    }
}
