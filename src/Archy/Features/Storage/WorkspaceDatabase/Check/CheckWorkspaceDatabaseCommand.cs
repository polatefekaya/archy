using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Check;

public sealed record CheckWorkspaceDatabaseCommand(WorkspaceStateLocation Location)
    : IRequest<Result<DatabaseIntegrityReport>>;
