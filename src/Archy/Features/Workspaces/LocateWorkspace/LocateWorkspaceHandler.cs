using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.LocateWorkspace;

public sealed class LocateWorkspaceHandler(IWorkspaceLocator workspaceLocator)
    : IRequestHandler<LocateWorkspaceCommand, Result<LocatedWorkspace>>
{
    public ValueTask<Result<LocatedWorkspace>> Handle(
        LocateWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(workspaceLocator.Locate(command.StartPath));
    }
}
