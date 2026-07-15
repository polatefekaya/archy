using Microsoft.Data.Sqlite;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.WorkspaceDatabase.Restore;

public sealed class WorkspaceDatabaseRestorer(
    TimeProvider timeProvider,
    IWorkspaceLockManager lockManager)
{
    public async ValueTask<Result<DatabaseRestore>> RestoreAsync(
        WorkspaceStateLocation location,
        string backupPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var managedBackupPath = NormalizeManagedBackupPath(location, backupPath);
        if (!managedBackupPath.IsSuccess)
        {
            return ResultFactory.Failure<DatabaseRestore>(managedBackupPath.Problem!);
        }

        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<DatabaseRestore>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        var temporaryPath = Path.Combine(location.StateDirectory, $"archy-restore-{Guid.NewGuid():N}.tmp");
        var replacedPath = Path.Combine(location.StateDirectory, $"archy-replaced-{Guid.NewGuid():N}.db");
        try
        {
            var backupSchemaVersion = await ValidateBackupAsync(
                managedBackupPath.Value,
                location.WorkspaceId,
                cancellationToken);
            if (!backupSchemaVersion.IsSuccess)
            {
                return ResultFactory.Failure<DatabaseRestore>(backupSchemaVersion.Problem!);
            }

            File.Copy(managedBackupPath.Value, temporaryPath, overwrite: false);
            SqliteConnection.ClearAllPools();
            DeleteIfPresent($"{location.DatabasePath}-wal");
            DeleteIfPresent($"{location.DatabasePath}-shm");
            if (File.Exists(location.DatabasePath))
            {
                File.Move(location.DatabasePath, replacedPath, overwrite: false);
            }

            try
            {
                File.Move(temporaryPath, location.DatabasePath, overwrite: false);
            }
            catch
            {
                if (File.Exists(replacedPath) && !File.Exists(location.DatabasePath))
                {
                    File.Move(replacedPath, location.DatabasePath, overwrite: false);
                }

                throw;
            }

            DeleteIfPresent($"{location.DatabasePath}-wal");
            DeleteIfPresent($"{location.DatabasePath}-shm");
            SqliteConnection.ClearAllPools();
            return ResultFactory.Success(new DatabaseRestore(
                managedBackupPath.Value,
                backupSchemaVersion.Value,
                timeProvider.GetUtcNow()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<DatabaseRestore>(
                Problem.Storage($"Archy could not restore the workspace database: {exception.Message}"));
        }
        finally
        {
            DeleteIfPresent(temporaryPath);
            DeleteIfPresent(replacedPath);
        }
    }

    private static async Task<Result<int>> ValidateBackupAsync(
        string backupPath,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(backupPath))
        {
            return ResultFactory.Failure<int>(
                Problem.NotFound("The requested managed database backup does not exist."));
        }

        try
        {
            await using var connection = await WorkspaceDatabaseSql.OpenAsync(
                backupPath,
                SqliteOpenMode.ReadOnly,
                cancellationToken);
            if (!await WorkspaceDatabaseSql.WorkspaceExistsAsync(connection, workspaceId, cancellationToken))
            {
                return ResultFactory.Failure<int>(
                    Problem.Conflict("The requested backup belongs to a different workspace."));
            }

            var report = await WorkspaceDatabaseIntegrityInspector.InspectAsync(connection, workspaceId, cancellationToken);
            return report.IsHealthy
                ? ResultFactory.Success(report.SchemaVersion)
                : ResultFactory.Failure<int>(
                    Problem.Conflict("The requested backup failed integrity validation and cannot be restored."));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 1 or 11 or 26)
        {
            return ResultFactory.Failure<int>(
                Problem.Conflict($"The requested backup failed integrity validation and cannot be restored: {exception.Message}"));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<int>(
                Problem.Storage($"Archy could not validate the requested backup: {exception.Message}"));
        }
    }

    private static Result<string> NormalizeManagedBackupPath(
        WorkspaceStateLocation location,
        string backupPath)
    {
        var backupDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(location.DatabaseBackupDirectory));
        var candidate = Path.GetFullPath(backupPath);
        var requiredPrefix = string.Concat(backupDirectory, Path.DirectorySeparatorChar);
        return candidate.StartsWith(requiredPrefix, StringComparison.Ordinal) &&
               string.Equals(Path.GetExtension(candidate), ".db", StringComparison.OrdinalIgnoreCase)
            ? ResultFactory.Success(candidate)
            : ResultFactory.Failure<string>(
                Problem.Validation("Restore accepts only a .db backup inside Archy's managed backup directory."));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
