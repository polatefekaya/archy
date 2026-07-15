using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Backup;

public sealed record BackupWorkspaceDatabaseCommand(WorkspaceStateLocation Location)
    : IRequest<Result<DatabaseBackup>>;
