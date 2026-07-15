namespace Archy.Features.Analysis.WatchWorkspaceChanges;

public interface IWorkspaceChangeDebounceController : IAsyncDisposable
{
    void RecordPath(string repositoryRelativePath);

    void RecordSessionActivity();

    void RecordWatcherFault();
}
