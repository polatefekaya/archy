using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.ReadActiveGraphRevision;

public sealed class ActiveGraphRevisionReader(IWorkspaceLockManager lockManager) : IActiveGraphRevisionReader
{
    public async ValueTask<Result<long?>> ReadAsync(
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
            return ResultFactory.Failure<long?>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = location.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT active_graph_revision FROM workspace_graph_states WHERE workspace_id = $workspaceId;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return ResultFactory.Success<long?>(value is null || value is DBNull
                ? null
                : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<long?>(
                Problem.Storage($"Archy could not read the active graph revision: {exception.Message}"));
        }
    }
}
