using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public interface IWorkspaceManifestStore
{
    ValueTask<Result<WorkspaceManifestWriteResult>> LoadOrCreateAsync(
        WorkspaceStateLocation location,
        LocatedWorkspace workspace,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceManifestWriteResult(WorkspaceManifest Manifest, bool WasCreated);
