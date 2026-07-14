using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.AcquireWorkspaceLock;

public interface IWorkspaceLockManager
{
    ValueTask<Result<IWorkspaceLockLease>> AcquireAsync(
        WorkspaceStateLocation location,
        WorkspaceLockMode mode,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
