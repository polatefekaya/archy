using Microsoft.Data.Sqlite;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.WorkspaceDatabase.Vacuum;

public sealed class WorkspaceDatabaseVacuumService(IWorkspaceLockManager lockManager)
{
    private const int FreePagePercentThreshold = 10;

    public async ValueTask<Result<DatabaseVacuumResult>> VacuumAsync(
        WorkspaceStateLocation location,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<DatabaseVacuumResult>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            await using var connection = await WorkspaceDatabaseSql.OpenAsync(
                location.DatabasePath,
                SqliteOpenMode.ReadWrite,
                cancellationToken);
            var pageCount = await WorkspaceDatabaseSql.ReadPragmaLongAsync(connection, "page_count", cancellationToken);
            var freelistPageCount = await WorkspaceDatabaseSql.ReadPragmaLongAsync(connection, "freelist_count", cancellationToken);
            if (!force && (pageCount == 0 || freelistPageCount * 100 < pageCount * FreePagePercentThreshold))
            {
                return ResultFactory.Success(new DatabaseVacuumResult(
                    WasVacuumed: false,
                    pageCount,
                    freelistPageCount,
                    $"Skipped because free pages are below the {FreePagePercentThreshold}% maintenance threshold."));
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
            return ResultFactory.Success(new DatabaseVacuumResult(
                WasVacuumed: true,
                pageCount,
                freelistPageCount,
                force ? "Vacuumed by explicit force request." : "Vacuumed because the free-page threshold was met."));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<DatabaseVacuumResult>(
                Problem.Storage($"Archy could not vacuum the workspace database: {exception.Message}"));
        }
    }
}
