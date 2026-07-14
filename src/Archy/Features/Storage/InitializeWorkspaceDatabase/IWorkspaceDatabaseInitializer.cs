using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.InitializeWorkspaceDatabase;

public interface IWorkspaceDatabaseInitializer
{
    ValueTask<Result<InitializedWorkspaceDatabase>> InitializeAsync(
        WorkspaceStateLocation location,
        WorkspaceManifest manifest,
        string configurationHash,
        CancellationToken cancellationToken);
}
