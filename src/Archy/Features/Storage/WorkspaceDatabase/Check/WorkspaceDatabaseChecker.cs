using Microsoft.Data.Sqlite;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.WorkspaceDatabase.Check;

public sealed class WorkspaceDatabaseChecker(IWorkspaceLockManager lockManager)
{
    public async ValueTask<Result<DatabaseIntegrityReport>> CheckAsync(
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
            return ResultFactory.Failure<DatabaseIntegrityReport>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            await using var connection = await WorkspaceDatabaseSql.OpenAsync(
                location.DatabasePath,
                SqliteOpenMode.ReadOnly,
                cancellationToken);
            return ResultFactory.Success(
                await WorkspaceDatabaseIntegrityInspector.InspectAsync(connection, location.WorkspaceId, cancellationToken));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<DatabaseIntegrityReport>(
                Problem.Storage($"Archy could not check the workspace database: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<DatabaseIntegrityReport>(
                Problem.Storage($"Archy could not check the workspace database: {exception.Message}"));
        }
    }
}
