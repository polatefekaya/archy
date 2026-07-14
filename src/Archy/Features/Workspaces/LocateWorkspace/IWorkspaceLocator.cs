using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.LocateWorkspace;

public interface IWorkspaceLocator
{
    Result<LocatedWorkspace> Locate(string startPath);
}
