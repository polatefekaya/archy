using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public interface IWorkspaceStateLayout
{
    Result<WorkspaceStateLocation> Resolve(LocatedWorkspace workspace, string? requestedStateRoot);
}
