using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.Backup;

public sealed class BackupWorkspaceDatabaseHandler(WorkspaceDatabaseBackupCreator backupCreator)
    : IRequestHandler<BackupWorkspaceDatabaseCommand, Result<DatabaseBackup>>
{
    public ValueTask<Result<DatabaseBackup>> Handle(
        BackupWorkspaceDatabaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return backupCreator.CreateAsync(command.Location, cancellationToken);
    }
}
