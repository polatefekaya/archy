using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Sessions.ReplayEventPages;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Sessions.ReplayEventPages;

public sealed class ArchitectureSessionEventReplayReaderTests
{
    [Fact]
    public async Task ReturnsAContiguousBoundedPageAndAnExplicitReconnectSignal()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var started = await repository.StartAsync(initialized.Value.StateLocation, new SessionStartFact("session-replay", "codex", "external:replay", "codex_agent", "fixture", "{}"), CancellationToken.None);
        Assert.True(started.IsSuccess);
        for (var index = 0; index < 3; index++)
        {
            var appended = await repository.AppendEventAsync(initialized.Value.StateLocation, "session-replay", new SessionEventFact(SessionEventKind.FileTouched, null, null, "{}"), CancellationToken.None);
            Assert.True(appended.IsSuccess);
        }

        var first = await repository.ReadAsync(initialized.Value.StateLocation, "session-replay", afterSequence: 0, maximumEvents: 2, CancellationToken.None);
        var second = await repository.ReadAsync(initialized.Value.StateLocation, "session-replay", afterSequence: 2, maximumEvents: 2, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal([1, 2], first.Value.Events.Select(static sessionEvent => sessionEvent.SequenceNumber));
        Assert.True(first.Value.HasMore);
        Assert.True(second.IsSuccess);
        Assert.Equal([3, 4], second.Value.Events.Select(static sessionEvent => sessionEvent.SequenceNumber));
        Assert.False(second.Value.HasMore);
    }

    [Fact]
    public async Task RejectsInvalidCursorAndPageSizeBeforeOpeningState()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var rejectedCursor = await repository.ReadAsync(initialized.Value.StateLocation, "session", -1, 1, CancellationToken.None);
        var rejectedLimit = await repository.ReadAsync(initialized.Value.StateLocation, "session", 0, 1_001, CancellationToken.None);

        Assert.False(rejectedCursor.IsSuccess);
        Assert.Equal("validation", rejectedCursor.Problem!.Code);
        Assert.False(rejectedLimit.IsSuccess);
        Assert.Equal("validation", rejectedLimit.Problem!.Code);
    }
}
