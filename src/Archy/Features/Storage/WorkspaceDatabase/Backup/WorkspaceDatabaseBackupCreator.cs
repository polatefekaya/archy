using Microsoft.Data.Sqlite;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.WorkspaceDatabase.Backup;

public sealed class WorkspaceDatabaseBackupCreator(
    TimeProvider timeProvider,
    IWorkspaceLockManager lockManager)
{
    public async ValueTask<Result<DatabaseBackup>> CreateAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Read,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<DatabaseBackup>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            await using var connection = await WorkspaceDatabaseSql.OpenAsync(
                location.DatabasePath,
                SqliteOpenMode.ReadWrite,
                cancellationToken);
            var schemaVersion = await WorkspaceDatabaseSql.ReadSchemaVersionAsync(connection, cancellationToken);
            var backupPath = await WorkspaceDatabaseBackup.CreateManualAsync(
                connection,
                location,
                timeProvider,
                cancellationToken);
            return ResultFactory.Success(new DatabaseBackup(
                backupPath,
                schemaVersion,
                timeProvider.GetUtcNow()));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<DatabaseBackup>(
                Problem.Storage($"Archy could not back up the workspace database: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<DatabaseBackup>(
                Problem.Storage($"Archy could not back up the workspace database: {exception.Message}"));
        }
    }
}
