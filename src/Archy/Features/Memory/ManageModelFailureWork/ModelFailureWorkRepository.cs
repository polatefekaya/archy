using System.Text.Json;
using Microsoft.Data.Sqlite;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.ManageModelFailureWork;

/// <summary>Durable retry/pause/disable state for model work. It never stores the prompt or API credential.</summary>
public sealed class ModelFailureWorkRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IModelFailureWorkRepository
{
    public async ValueTask<Result<ModelFailureWorkItem>> EnqueueRetryableAsync(WorkspaceStateLocation location, ModelFailureWorkFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var validation = Validate(fact);
        if (validation is not null)
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(validation);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (!await BelongsToWorkspaceAsync(connection, transaction, "graph_revisions", "revision", fact.SourceGraphRevision, location.WorkspaceId, cancellationToken))
            {
                return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Conflict("Model retry work must reference a graph revision in the same workspace."));
            }

            if (fact.SummaryBatchId is not null && !await BatchMatchesRevisionAsync(connection, transaction, fact.SummaryBatchId, fact.SourceGraphRevision, location.WorkspaceId, cancellationToken))
            {
                return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Conflict("Model retry work must reference a summary batch from the same workspace and graph revision."));
            }

            var exists = await ScalarLongAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM model_failure_work_items WHERE work_item_id = $workItemId);", cancellationToken, ("$workItemId", fact.WorkItemId));
            if (exists != 0)
            {
                return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Conflict($"Model work item '{fact.WorkItemId}' already exists."));
            }

            var now = timeProvider.GetUtcNow();
            await ExecuteAsync(connection, transaction, "INSERT INTO model_failure_work_items(work_item_id, workspace_id, work_kind, summary_batch_id, target_stable_id, source_graph_revision, state, failure_kind, attempt_count, next_attempt_at_utc, metadata_json, created_at_utc, updated_at_utc) VALUES ($workItemId, $workspaceId, $workKind, $summaryBatchId, $targetStableId, $sourceGraphRevision, 'pending', $failureKind, $attemptCount, $nextAttemptAt, $metadataJson, $now, $now);", cancellationToken,
                ("$workItemId", fact.WorkItemId), ("$workspaceId", location.WorkspaceId), ("$workKind", ToDatabase(fact.Kind)), ("$summaryBatchId", fact.SummaryBatchId), ("$targetStableId", fact.TargetStableId), ("$sourceGraphRevision", fact.SourceGraphRevision), ("$failureKind", fact.Failure.Kind.ToString()), ("$attemptCount", fact.AttemptCount), ("$nextAttemptAt", fact.NextAttemptAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), ("$metadataJson", fact.MetadataJson), ("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            await ExecuteAsync(connection, transaction, "INSERT INTO model_failure_work_events(work_item_id, event_ordinal, state, reason_json, occurred_at_utc) VALUES ($workItemId, 1, 'pending', $reasonJson, $now);", cancellationToken,
                ("$workItemId", fact.WorkItemId), ("$reasonJson", $"{{\"failureKind\":\"{fact.Failure.Kind}\"}}"), ("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToItem(fact, now));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Storage($"Archy could not persist model retry work: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<ModelFailureWorkItem>>> ListActiveAsync(WorkspaceStateLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ModelFailureWorkItem>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT work_item_id, work_kind, summary_batch_id, target_stable_id, source_graph_revision, state, failure_kind, attempt_count, next_attempt_at_utc, metadata_json, created_at_utc, updated_at_utc FROM model_failure_work_items WHERE workspace_id = $workspaceId AND state <> 'completed' ORDER BY COALESCE(next_attempt_at_utc, created_at_utc), created_at_utc, work_item_id;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<ModelFailureWorkItem>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadItem(reader));
            }

            return ResultFactory.Success<IReadOnlyList<ModelFailureWorkItem>>([.. items]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<ModelFailureWorkItem>>(Problem.Storage($"Archy could not read model retry work: {exception.Message}"));
        }
    }

    public async ValueTask<Result<ModelFailureWorkItem>> SetStateAsync(WorkspaceStateLocation location, string workItemId, ModelFailureWorkState state, string reasonJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        if (!Enum.IsDefined(state) || !IsJson(reasonJson))
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Validation("Model retry state transitions require a known state and JSON reason."));
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var current = await ReadItemAsync(connection, transaction, location.WorkspaceId, workItemId, cancellationToken);
            if (current is null)
            {
                return ResultFactory.Failure<ModelFailureWorkItem>(Problem.NotFound($"Model work item '{workItemId}' was not found."));
            }

            if (current.State == ModelFailureWorkState.Completed && state != ModelFailureWorkState.Completed)
            {
                return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Conflict("Completed model work is immutable; create new work for a later failure."));
            }

            var now = timeProvider.GetUtcNow();
            var nextAttemptAt = state == ModelFailureWorkState.Pending ? null : current.NextAttemptAtUtc;
            await ExecuteAsync(connection, transaction, "UPDATE model_failure_work_items SET state = $state, next_attempt_at_utc = $nextAttemptAt, updated_at_utc = $now WHERE work_item_id = $workItemId;", cancellationToken,
                ("$state", ToDatabase(state)), ("$nextAttemptAt", nextAttemptAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), ("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), ("$workItemId", workItemId));
            var ordinal = await ScalarLongAsync(connection, transaction, "SELECT COALESCE(MAX(event_ordinal), 0) + 1 FROM model_failure_work_events WHERE work_item_id = $workItemId;", cancellationToken, ("$workItemId", workItemId));
            await ExecuteAsync(connection, transaction, "INSERT INTO model_failure_work_events(work_item_id, event_ordinal, state, reason_json, occurred_at_utc) VALUES ($workItemId, $ordinal, $state, $reasonJson, $now);", cancellationToken,
                ("$workItemId", workItemId), ("$ordinal", ordinal), ("$state", ToDatabase(state)), ("$reasonJson", reasonJson), ("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(current with { State = state, NextAttemptAtUtc = nextAttemptAt, UpdatedAtUtc = now });
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ModelFailureWorkItem>(Problem.Storage($"Archy could not update model retry work: {exception.Message}"));
        }
    }

    private static Problem? Validate(ModelFailureWorkFact fact)
    {
        if (string.IsNullOrWhiteSpace(fact.WorkItemId) || string.IsNullOrWhiteSpace(fact.TargetStableId) || fact.SourceGraphRevision < 1 ||
            fact.AttemptCount < 1 || !Enum.IsDefined(fact.Kind) || fact.Failure is null || !Enum.IsDefined(fact.Failure.Kind) ||
            !fact.Failure.IsRetryable || !IsJson(fact.MetadataJson) || (fact.SummaryBatchId is not null && string.IsNullOrWhiteSpace(fact.SummaryBatchId)))
        {
            return Problem.Validation("Model retry work requires a retryable provider failure, immutable graph context, and JSON metadata.");
        }

        return null;
    }

    private static ModelFailureWorkItem ToItem(ModelFailureWorkFact fact, DateTimeOffset now) => new(fact.WorkItemId, fact.Kind, fact.SummaryBatchId, fact.TargetStableId, fact.SourceGraphRevision, ModelFailureWorkState.Pending, fact.Failure.Kind, fact.AttemptCount, fact.NextAttemptAtUtc, fact.MetadataJson, now, now);

    private static ModelFailureWorkItem ReadItem(SqliteDataReader reader) => new(reader.GetString(0), FromKind(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetInt64(4), FromState(reader.GetString(5)), Enum.Parse<ModelProviderFailureKind>(reader.GetString(6), ignoreCase: true), reader.GetInt32(7), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(9), DateTimeOffset.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(11), System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<ModelFailureWorkItem?> ReadItemAsync(SqliteConnection connection, SqliteTransaction transaction, string workspaceId, string workItemId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT work_item_id, work_kind, summary_batch_id, target_stable_id, source_graph_revision, state, failure_kind, attempt_count, next_attempt_at_utc, metadata_json, created_at_utc, updated_at_utc FROM model_failure_work_items WHERE workspace_id = $workspaceId AND work_item_id = $workItemId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$workItemId", workItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    private static async Task<bool> BelongsToWorkspaceAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string idColumn, long id, string workspaceId, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, $"SELECT EXISTS(SELECT 1 FROM {table} WHERE {idColumn} = $id AND workspace_id = $workspaceId);", cancellationToken, ("$id", id), ("$workspaceId", workspaceId)) != 0;

    private static async Task<bool> BatchMatchesRevisionAsync(SqliteConnection connection, SqliteTransaction transaction, string batchId, long revision, string workspaceId, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM summary_batches WHERE summary_batch_id = $batchId AND workspace_id = $workspaceId AND source_graph_revision = $revision);", cancellationToken, ("$batchId", batchId), ("$workspaceId", workspaceId), ("$revision", revision)) != 0;

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsJson(string value) { try { using var _ = JsonDocument.Parse(value); return true; } catch (JsonException) { return false; } }
    private static string ToDatabase(ModelFailureWorkKind kind) => kind == ModelFailureWorkKind.Summary ? "summary" : "embedding";
    private static ModelFailureWorkKind FromKind(string value) => value == "summary" ? ModelFailureWorkKind.Summary : value == "embedding" ? ModelFailureWorkKind.Embedding : throw new InvalidOperationException($"Unknown model work kind '{value}'.");
    private static string ToDatabase(ModelFailureWorkState state) => state.ToString().ToLowerInvariant();
    private static ModelFailureWorkState FromState(string value) => Enum.TryParse<ModelFailureWorkState>(value, true, out var state) ? state : throw new InvalidOperationException($"Unknown model work state '{value}'.");
}
