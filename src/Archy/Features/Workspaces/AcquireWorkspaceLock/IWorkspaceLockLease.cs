namespace Archy.Features.Workspaces.AcquireWorkspaceLock;

public interface IWorkspaceLockLease : IDisposable
{
    WorkspaceLockMode Mode { get; }
}
