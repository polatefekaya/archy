namespace Archy.Features.Analysis.WatchWorkspaceChanges;

public sealed record WorkspaceWatchOptions(TimeSpan SettleWindow, TimeSpan SessionIdleWindow)
{
    public static WorkspaceWatchOptions Default { get; } = new(
        SettleWindow: TimeSpan.FromMilliseconds(750),
        SessionIdleWindow: TimeSpan.FromSeconds(2));
}
