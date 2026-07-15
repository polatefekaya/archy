using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Vacuum;

public sealed record VacuumWorkspaceDatabaseCommand(
    WorkspaceStateLocation Location,
    bool Force)
    : IRequest<Result<DatabaseVacuumResult>>;
