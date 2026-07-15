using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Vacuum;

public sealed class VacuumWorkspaceDatabaseHandler(WorkspaceDatabaseVacuumService vacuumService)
    : IRequestHandler<VacuumWorkspaceDatabaseCommand, Result<DatabaseVacuumResult>>
{
    public ValueTask<Result<DatabaseVacuumResult>> Handle(
        VacuumWorkspaceDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return vacuumService.VacuumAsync(command.Location, command.Force, cancellationToken);
    }
}
