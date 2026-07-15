using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Restore;

public sealed record RestoreWorkspaceDatabaseCommand(
    WorkspaceStateLocation Location,
    string BackupPath)
    : IRequest<Result<DatabaseRestore>>;
