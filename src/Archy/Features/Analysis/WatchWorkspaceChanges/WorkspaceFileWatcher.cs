using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.WatchWorkspaceChanges;

/// <summary>Turns OS file notifications into scoped reconciliation hints; inventory remains authoritative.</summary>
public sealed class WorkspaceFileWatcher : IWorkspaceFileWatcher
{
    public Result<IAsyncDisposable> Start(
        WorkspaceWatchRequest request,
        IWorkspaceChangeDebounceController debounceController)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(debounceController);
        if (string.IsNullOrWhiteSpace(request.RepositoryRoot) || !Directory.Exists(request.RepositoryRoot))
        {
            return ResultFactory.Failure<IAsyncDisposable>(Problem.NotFound("The workspace watcher requires an existing repository root."));
        }

        try
        {
            var watcher = new FileSystemWatcher(Path.GetFullPath(request.RepositoryRoot))
            {
                IncludeSubdirectories = true,
                Filter = "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 32 * 1024,
                EnableRaisingEvents = false,
            };
            var subscription = new WorkspaceFileWatcherSubscription(watcher, request, debounceController);
            subscription.Enable();
            return ResultFactory.Success<IAsyncDisposable>(subscription);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ResultFactory.Failure<IAsyncDisposable>(
                Problem.Storage($"Archy could not start the workspace file watcher: {exception.Message}"));
        }
    }

    private sealed class WorkspaceFileWatcherSubscription : IAsyncDisposable
    {
        private readonly FileSystemWatcher watcher;
        private readonly WorkspaceWatchRequest request;
        private readonly IWorkspaceChangeDebounceController debounceController;
        private bool disposed;

        internal WorkspaceFileWatcherSubscription(
            FileSystemWatcher watcher,
            WorkspaceWatchRequest request,
            IWorkspaceChangeDebounceController debounceController)
        {
            this.watcher = watcher;
            this.request = request;
            this.debounceController = debounceController;
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnChanged;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnError;
            watcher.Dispose();
            return ValueTask.CompletedTask;
        }

        internal void Enable() => watcher.EnableRaisingEvents = true;

        private void OnChanged(object sender, FileSystemEventArgs eventArgs) => RecordIfInScope(eventArgs.FullPath);

        private void OnRenamed(object sender, RenamedEventArgs eventArgs)
        {
            RecordIfInScope(eventArgs.OldFullPath);
            RecordIfInScope(eventArgs.FullPath);
        }

        private void OnError(object sender, ErrorEventArgs eventArgs) => debounceController.RecordWatcherFault();

        private void RecordIfInScope(string fullPath)
        {
            try
            {
                var relativePath = Path.GetRelativePath(request.RepositoryRoot, fullPath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (relativePath is "." or ".." || relativePath.StartsWith("../", StringComparison.Ordinal))
                {
                    return;
                }

                if (request.Scope.Explain(relativePath, isDirectory: false).IsIncluded)
                {
                    debounceController.RecordPath(relativePath);
                }
            }
            catch (ObjectDisposedException)
            {
                // The subscription is shutting down while the platform delivers a final notification.
            }
            catch (ArgumentException)
            {
                debounceController.RecordWatcherFault();
            }
        }
    }
}
