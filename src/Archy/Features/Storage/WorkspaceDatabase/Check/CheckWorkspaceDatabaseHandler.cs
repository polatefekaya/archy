using Archy.SharedKernel.Primitives;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Check;

public sealed class CheckWorkspaceDatabaseHandler(WorkspaceDatabaseChecker checker)
    : IRequestHandler<CheckWorkspaceDatabaseCommand, Result<DatabaseIntegrityReport>>
{
    public ValueTask<Result<DatabaseIntegrityReport>> Handle(
        CheckWorkspaceDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return checker.CheckAsync(command.Location, cancellationToken);
    }
}
