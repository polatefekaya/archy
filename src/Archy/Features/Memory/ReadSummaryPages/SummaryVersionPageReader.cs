using Archy.Features.Memory.Summaries;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Memory.ReadSummaryPages;

/// <summary>Reads bounded summary history directly from append-only storage.</summary>
public sealed class SummaryVersionPageReader(IWorkspaceLockManager lockManager) : ISummaryVersionPageReader
{
    public async ValueTask<Result<SummaryVersionPage>> ReadAsync(WorkspaceStateLocation location, string summaryId, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(summaryId) || offset is < 0 or > 1_000_000 || limit is < 1 or > 50)
        {
            return ResultFactory.Failure<SummaryVersionPage>(Problem.Validation("Summary history requires an identity, an offset between 0 and 1000000, and a limit between 1 and 50."));
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<SummaryVersionPage>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var total = await CountAsync(connection, location.WorkspaceId, summaryId, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT summary_version_id, version_number, source_graph_revision, source_repository_commit, summary_text, english_diff, provider, model, provider_metadata_json, staleness, supersedes_summary_version_id, created_at_utc, originating_summary_batch_id FROM summary_versions WHERE workspace_id = $workspaceId AND summary_id = $summaryId ORDER BY version_number LIMIT $limit OFFSET $offset;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$summaryId", summaryId);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<SummaryVersion>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new SummaryVersion(reader.GetString(0), summaryId, reader.GetInt32(1), reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), ParseStaleness(reader.GetString(9)), reader.IsDBNull(10) ? null : reader.GetString(10), DateTimeOffset.Parse(reader.GetString(11), System.Globalization.CultureInfo.InvariantCulture), reader.IsDBNull(12) ? null : reader.GetString(12)));
            }

            return ResultFactory.Success(new SummaryVersionPage(summaryId, offset, limit, total, [.. items]));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SummaryVersionPage>(Problem.Storage($"Archy could not read summary history: {exception.Message}"));
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string workspaceId, string summaryId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM summary_versions WHERE workspace_id = $workspaceId AND summary_id = $summaryId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$summaryId", summaryId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SummaryStaleness ParseStaleness(string value) =>
        Enum.TryParse<SummaryStaleness>(value, ignoreCase: true, out var staleness)
            ? staleness
            : throw new InvalidOperationException($"Unknown persisted summary staleness '{value}'.");
}
