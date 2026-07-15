namespace Archy.Features.Analysis.WatchWorkspaceChanges;

/// <summary>
/// Coalesces watcher noise into serial inventory reconciliation. A failed dispatch is re-queued,
/// so a final edit cannot be silently lost while a prior analysis is running.
/// </summary>
public sealed class WorkspaceChangeDebounceController : IWorkspaceChangeDebounceController
{
    private readonly object gate = new();
    private readonly TimeProvider timeProvider;
    private readonly WorkspaceWatchOptions options;
    private readonly Func<WorkspaceChangeSet, CancellationToken, ValueTask> dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Timer timer;
    private readonly SortedSet<string> pendingPaths = new(StringComparer.Ordinal);
    private DateTimeOffset lastChangeAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset lastSessionActivityAtUtc = DateTimeOffset.MinValue;
    private bool requiresFullInventoryReconciliation;
    private bool isDispatching;
    private bool isDisposed;
    private Task? activeDispatch;

    public WorkspaceChangeDebounceController(
        TimeProvider timeProvider,
        WorkspaceWatchOptions options,
        Func<WorkspaceChangeSet, CancellationToken, ValueTask> dispatcher)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        if (options.SettleWindow <= TimeSpan.Zero || options.SessionIdleWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Watcher settle and session-idle windows must be non-negative, with a positive settle window.");
        }

        timer = new Timer(static state => ((WorkspaceChangeDebounceController)state!).OnTimer(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void RecordPath(string repositoryRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRelativePath);
        lock (gate)
        {
            ThrowIfDisposed();
            pendingPaths.Add(NormalizePath(repositoryRelativePath));
            lastChangeAtUtc = timeProvider.GetUtcNow();
            ScheduleLocked();
        }
    }

    public void RecordSessionActivity()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            lastSessionActivityAtUtc = timeProvider.GetUtcNow();
            if (pendingPaths.Count > 0 || requiresFullInventoryReconciliation)
            {
                ScheduleLocked();
            }
        }
    }

    public void RecordWatcherFault()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            requiresFullInventoryReconciliation = true;
            lastChangeAtUtc = timeProvider.GetUtcNow();
            ScheduleLocked();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? dispatch;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Dispose();
            lifetime.Cancel();
            dispatch = activeDispatch;
        }

        if (dispatch is not null)
        {
            try
            {
                await dispatch;
            }
            catch (OperationCanceledException)
            {
                // Disposal owns the cancellation boundary.
            }
        }

        lifetime.Dispose();
    }

    private void OnTimer()
    {
        lock (gate)
        {
            if (isDisposed || isDispatching || (pendingPaths.Count == 0 && !requiresFullInventoryReconciliation))
            {
                return;
            }

            if (!IsSettledLocked())
            {
                ScheduleLocked();
                return;
            }

            var changeSet = new WorkspaceChangeSet(
                timeProvider.GetUtcNow(),
                [.. pendingPaths],
                requiresFullInventoryReconciliation);
            pendingPaths.Clear();
            requiresFullInventoryReconciliation = false;
            isDispatching = true;
            activeDispatch = DispatchAsync(changeSet);
        }
    }

    private async Task DispatchAsync(WorkspaceChangeSet changeSet)
    {
        try
        {
            await dispatcher(changeSet, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            lock (gate)
            {
                if (!isDisposed)
                {
                    foreach (var path in changeSet.RepositoryRelativePaths)
                    {
                        pendingPaths.Add(path);
                    }

                    requiresFullInventoryReconciliation |= changeSet.RequiresFullInventoryReconciliation;
                    lastChangeAtUtc = timeProvider.GetUtcNow();
                }
            }
        }
        finally
        {
            lock (gate)
            {
                isDispatching = false;
                activeDispatch = null;
                if (!isDisposed && (pendingPaths.Count > 0 || requiresFullInventoryReconciliation))
                {
                    ScheduleLocked();
                }
            }
        }
    }

    private bool IsSettledLocked()
    {
        var now = timeProvider.GetUtcNow();
        return now >= lastChangeAtUtc + options.SettleWindow &&
               now >= lastSessionActivityAtUtc + options.SessionIdleWindow;
    }

    private void ScheduleLocked()
    {
        if (isDisposed || isDispatching)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var eligibleAt = Max(lastChangeAtUtc + options.SettleWindow, lastSessionActivityAtUtc + options.SessionIdleWindow);
        var due = eligibleAt > now ? eligibleAt - now : TimeSpan.Zero;
        timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
