using System.Text.Json;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.Summaries;

public sealed class SummaryRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : ISummaryRepository
{
    public async ValueTask<Result<SummaryVersion>> AppendAsync(
        WorkspaceStateLocation location,
        SummaryVersionFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<SummaryVersion>(invalid);
        }

        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SummaryVersion>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var sourceRevisionWorkspaceId = await ScalarStringAsync(
                connection,
                transaction,
                "SELECT workspace_id FROM graph_revisions WHERE revision = $revision;",
                cancellationToken,
                ("$revision", fact.SourceGraphRevision));
            if (!string.Equals(sourceRevisionWorkspaceId, location.WorkspaceId, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<SummaryVersion>(
                    Problem.Conflict("A summary must reference an existing graph revision in the same workspace."));
            }

            if (fact.OriginatingSummaryBatchId is not null)
            {
                var batchMatchesSourceRevision = await ScalarLongAsync(
                    connection,
                    transaction,
                    "SELECT EXISTS(SELECT 1 FROM summary_batches WHERE workspace_id = $workspaceId AND summary_batch_id = $summaryBatchId AND source_graph_revision = $sourceGraphRevision);",
                    cancellationToken,
                    ("$workspaceId", location.WorkspaceId),
                    ("$summaryBatchId", fact.OriginatingSummaryBatchId),
                    ("$sourceGraphRevision", fact.SourceGraphRevision));
                if (batchMatchesSourceRevision == 0)
                {
                    return ResultFactory.Failure<SummaryVersion>(
                        Problem.Conflict("A summary version must reference a summary batch from the same workspace and source graph revision."));
                }
            }

            var identity = await LoadIdentityAsync(connection, transaction, location.WorkspaceId, fact.SummaryId, cancellationToken);
            if (identity is not null &&
                (!string.Equals(identity.TargetKind, fact.TargetKind, StringComparison.Ordinal) ||
                 !string.Equals(identity.TargetStableId, fact.TargetStableId, StringComparison.Ordinal)))
            {
                return ResultFactory.Failure<SummaryVersion>(
                    Problem.Conflict($"Summary '{fact.SummaryId}' changed its immutable target."));
            }

            var createdAt = timeProvider.GetUtcNow();
            if (identity is null)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO summary_identities(workspace_id, summary_id, target_kind, target_stable_id, created_at_utc, latest_summary_version_id) VALUES ($workspaceId, $summaryId, $targetKind, $targetStableId, $createdAt, NULL);",
                    cancellationToken,
                    ("$workspaceId", location.WorkspaceId),
                    ("$summaryId", fact.SummaryId),
                    ("$targetKind", fact.TargetKind),
                    ("$targetStableId", fact.TargetStableId),
                    ("$createdAt", createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            }

            var versionNumber = await ScalarLongAsync(
                connection,
                transaction,
                "SELECT COALESCE(MAX(version_number), 0) + 1 FROM summary_versions WHERE workspace_id = $workspaceId AND summary_id = $summaryId;",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$summaryId", fact.SummaryId));
            var summaryVersionId = Guid.NewGuid().ToString("N");
            var supersedesSummaryVersionId = identity?.LatestSummaryVersionId;
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO summary_versions(summary_version_id, workspace_id, summary_id, version_number, source_graph_revision, source_repository_commit, summary_text, english_diff, provider, model, provider_metadata_json, staleness, supersedes_summary_version_id, created_at_utc, originating_summary_batch_id) VALUES ($summaryVersionId, $workspaceId, $summaryId, $versionNumber, $sourceGraphRevision, $sourceRepositoryCommit, $summaryText, $englishDiff, $provider, $model, $providerMetadataJson, $staleness, $supersedesSummaryVersionId, $createdAt, $originatingSummaryBatchId);",
                cancellationToken,
                ("$summaryVersionId", summaryVersionId),
                ("$workspaceId", location.WorkspaceId),
                ("$summaryId", fact.SummaryId),
                ("$versionNumber", versionNumber),
                ("$sourceGraphRevision", fact.SourceGraphRevision),
                ("$sourceRepositoryCommit", fact.SourceRepositoryCommit),
                ("$summaryText", fact.SummaryText),
                ("$englishDiff", fact.EnglishDiff),
                ("$provider", fact.Provider),
                ("$model", fact.Model),
                ("$providerMetadataJson", fact.ProviderMetadataJson),
                ("$staleness", ToDatabase(fact.Staleness)),
                ("$supersedesSummaryVersionId", supersedesSummaryVersionId),
                ("$createdAt", createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ("$originatingSummaryBatchId", fact.OriginatingSummaryBatchId));
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE summary_identities SET latest_summary_version_id = $summaryVersionId WHERE workspace_id = $workspaceId AND summary_id = $summaryId;",
                cancellationToken,
                ("$summaryVersionId", summaryVersionId),
                ("$workspaceId", location.WorkspaceId),
                ("$summaryId", fact.SummaryId));
            await transaction.CommitAsync(cancellationToken);

            return ResultFactory.Success(new SummaryVersion(
                summaryVersionId,
                fact.SummaryId,
                checked((int)versionNumber),
                fact.SourceGraphRevision,
                fact.SourceRepositoryCommit,
                fact.SummaryText,
                fact.EnglishDiff,
                fact.Provider,
                fact.Model,
                fact.ProviderMetadataJson,
                fact.Staleness,
                supersedesSummaryVersionId,
                createdAt,
                fact.OriginatingSummaryBatchId));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SummaryVersion>(
                Problem.Storage($"Archy could not append the summary version: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<SummaryVersion>>> ListAsync(
        WorkspaceStateLocation location,
        string summaryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryId);
        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Read,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<SummaryVersion>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT summary_version_id, version_number, source_graph_revision, source_repository_commit, summary_text, english_diff, provider, model, provider_metadata_json, staleness, supersedes_summary_version_id, created_at_utc, originating_summary_batch_id FROM summary_versions WHERE workspace_id = $workspaceId AND summary_id = $summaryId ORDER BY version_number;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$summaryId", summaryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var versions = new List<SummaryVersion>();
            while (await reader.ReadAsync(cancellationToken))
            {
                versions.Add(new SummaryVersion(
                    reader.GetString(0),
                    summaryId,
                    reader.GetInt32(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    FromDatabase(reader.GetString(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    DateTimeOffset.Parse(reader.GetString(11), System.Globalization.CultureInfo.InvariantCulture),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
            }

            return ResultFactory.Success<IReadOnlyList<SummaryVersion>>([.. versions]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<SummaryVersion>>(
                Problem.Storage($"Archy could not read summary versions: {exception.Message}"));
        }
    }

    private static Problem? Validate(SummaryVersionFact fact)
    {
        if (string.IsNullOrWhiteSpace(fact.SummaryId) ||
            string.IsNullOrWhiteSpace(fact.TargetKind) ||
            string.IsNullOrWhiteSpace(fact.TargetStableId) ||
            fact.SourceGraphRevision < 1 ||
            string.IsNullOrWhiteSpace(fact.SummaryText) ||
            string.IsNullOrWhiteSpace(fact.EnglishDiff) ||
            string.IsNullOrWhiteSpace(fact.Provider) ||
            string.IsNullOrWhiteSpace(fact.Model) ||
            !IsJson(fact.ProviderMetadataJson) ||
            !Enum.IsDefined(fact.Staleness) ||
            (fact.OriginatingSummaryBatchId is not null && string.IsNullOrWhiteSpace(fact.OriginatingSummaryBatchId)))
        {
            return Problem.Validation(
                "Summary versions require an immutable target, source graph revision, text, English diff, and provider metadata.");
        }

        return null;
    }

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<SummaryIdentity?> LoadIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string summaryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT target_kind, target_stable_id, latest_summary_version_id FROM summary_identities WHERE workspace_id = $workspaceId AND summary_id = $summaryId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$summaryId", summaryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SummaryIdentity(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarStringAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToDatabase(SummaryStaleness staleness) => staleness.ToString().ToLowerInvariant();

    private static SummaryStaleness FromDatabase(string value) =>
        Enum.TryParse<SummaryStaleness>(value, ignoreCase: true, out var staleness)
            ? staleness
            : throw new InvalidOperationException($"Unknown persisted summary staleness '{value}'.");

    private sealed record SummaryIdentity(string TargetKind, string TargetStableId, string? LatestSummaryVersionId);
}
