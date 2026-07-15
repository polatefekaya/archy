using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.WatchWorkspaceChanges;

public interface IWorkspaceFileWatcher
{
    Result<IAsyncDisposable> Start(
        WorkspaceWatchRequest request,
        IWorkspaceChangeDebounceController debounceController);
}
