using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Restore;

public sealed class RestoreWorkspaceDatabaseHandler(WorkspaceDatabaseRestorer restorer)
    : IRequestHandler<RestoreWorkspaceDatabaseCommand, Result<DatabaseRestore>>
{
    public ValueTask<Result<DatabaseRestore>> Handle(
        RestoreWorkspaceDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return restorer.RestoreAsync(command.Location, command.BackupPath, cancellationToken);
    }
}
