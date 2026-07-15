using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.SummaryBatches;

public sealed partial class SummaryBatchRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : ISummaryBatchRepository
{
    public async ValueTask<Result<SummaryBatch>> CreateAsync(
        WorkspaceStateLocation location,
        SummaryBatchFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = SummaryBatchValidator.Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<SummaryBatch>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SummaryBatch>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var sessionWorkspaceId = await SummaryBatchDatabase.ScalarStringAsync(
                connection,
                transaction,
                "SELECT workspace_id FROM sessions WHERE session_id = $sessionId;",
                cancellationToken,
                ("$sessionId", fact.SessionId));
            if (!string.Equals(sessionWorkspaceId, location.WorkspaceId, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<SummaryBatch>(
                    Problem.Conflict("A summary batch must reference an existing session in the same workspace."));
            }

            var revisionWorkspaceId = await SummaryBatchDatabase.ScalarStringAsync(
                connection,
                transaction,
                "SELECT workspace_id FROM graph_revisions WHERE revision = $revision;",
                cancellationToken,
                ("$revision", fact.SourceGraphRevision));
            if (!string.Equals(revisionWorkspaceId, location.WorkspaceId, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<SummaryBatch>(
                    Problem.Conflict("A summary batch must reference an existing graph revision in the same workspace."));
            }

            var existingWorkspaceId = await SummaryBatchDatabase.ScalarStringAsync(
                connection,
                transaction,
                "SELECT workspace_id FROM summary_batches WHERE summary_batch_id = $summaryBatchId;",
                cancellationToken,
                ("$summaryBatchId", fact.SummaryBatchId));
            if (existingWorkspaceId is not null)
            {
                return ResultFactory.Failure<SummaryBatch>(
                    Problem.Conflict($"Summary batch '{fact.SummaryBatchId}' already exists and is immutable."));
            }

            var createdAt = timeProvider.GetUtcNow();
            await SummaryBatchDatabase.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO summary_batches(summary_batch_id, workspace_id, session_id, settle_reason, request_state, source_graph_revision, model_request_metadata_json, created_at_utc) VALUES ($summaryBatchId, $workspaceId, $sessionId, $settleReason, $requestState, $sourceGraphRevision, $modelRequestMetadataJson, $createdAt);",
                cancellationToken,
                ("$summaryBatchId", fact.SummaryBatchId),
                ("$workspaceId", location.WorkspaceId),
                ("$sessionId", fact.SessionId),
                ("$settleReason", fact.SettleReason),
                ("$requestState", ToDatabase(fact.RequestState)),
                ("$sourceGraphRevision", fact.SourceGraphRevision),
                ("$modelRequestMetadataJson", fact.ModelRequestMetadataJson),
                ("$createdAt", ToDatabaseTime(createdAt)));
            for (var index = 0; index < fact.Members.Count; index++)
            {
                var member = fact.Members[index];
                await SummaryBatchDatabase.ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO summary_batch_members(summary_batch_id, member_ordinal, target_kind, target_stable_id, touch_ordinal, co_touched_member_ordinals_json) VALUES ($summaryBatchId, $memberOrdinal, $targetKind, $targetStableId, $touchOrdinal, $coTouchedMemberOrdinalsJson);",
                    cancellationToken,
                    ("$summaryBatchId", fact.SummaryBatchId),
                    ("$memberOrdinal", index + 1),
                    ("$targetKind", member.TargetKind),
                    ("$targetStableId", member.TargetStableId),
                    ("$touchOrdinal", member.TouchOrdinal),
                    ("$coTouchedMemberOrdinalsJson", JsonSerializer.Serialize(member.CoTouchedMemberOrdinals.ToArray(), SummaryBatchJsonContext.Default.Int32Array)));
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToSummaryBatch(fact, createdAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SummaryBatch>(
                Problem.Storage($"Archy could not persist the summary batch: {exception.Message}"));
        }
    }

    public async ValueTask<Result<SummaryBatch>> GetAsync(
        WorkspaceStateLocation location,
        string summaryBatchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryBatchId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SummaryBatch>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var batchCommand = connection.CreateCommand();
            batchCommand.CommandText = "SELECT session_id, settle_reason, request_state, source_graph_revision, model_request_metadata_json, created_at_utc FROM summary_batches WHERE workspace_id = $workspaceId AND summary_batch_id = $summaryBatchId;";
            batchCommand.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            batchCommand.Parameters.AddWithValue("$summaryBatchId", summaryBatchId);
            await using var batchReader = await batchCommand.ExecuteReaderAsync(cancellationToken);
            if (!await batchReader.ReadAsync(cancellationToken))
            {
                return ResultFactory.Failure<SummaryBatch>(Problem.NotFound($"Summary batch '{summaryBatchId}' was not found."));
            }

            var sessionId = batchReader.GetString(0);
            var settleReason = batchReader.GetString(1);
            var requestState = FromDatabase(batchReader.GetString(2));
            var sourceGraphRevision = batchReader.GetInt64(3);
            var requestMetadata = batchReader.GetString(4);
            var createdAt = FromDatabaseTime(batchReader.GetString(5));
            await batchReader.DisposeAsync();

            await using var memberCommand = connection.CreateCommand();
            memberCommand.CommandText = "SELECT member_ordinal, target_kind, target_stable_id, touch_ordinal, co_touched_member_ordinals_json FROM summary_batch_members WHERE summary_batch_id = $summaryBatchId ORDER BY member_ordinal;";
            memberCommand.Parameters.AddWithValue("$summaryBatchId", summaryBatchId);
            await using var memberReader = await memberCommand.ExecuteReaderAsync(cancellationToken);
            var members = new List<SummaryBatchMember>();
            while (await memberReader.ReadAsync(cancellationToken))
            {
                var coTouched = JsonSerializer.Deserialize(
                    memberReader.GetString(4),
                    SummaryBatchJsonContext.Default.Int32Array) ?? [];
                members.Add(new SummaryBatchMember(
                    memberReader.GetInt32(0),
                    memberReader.GetString(1),
                    memberReader.GetString(2),
                    memberReader.GetInt32(3),
                    coTouched));
            }

            return ResultFactory.Success(new SummaryBatch(
                summaryBatchId,
                sessionId,
                settleReason,
                requestState,
                sourceGraphRevision,
                requestMetadata,
                createdAt,
                members));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SummaryBatch>(
                Problem.Storage($"Archy could not read the summary batch: {exception.Message}"));
        }
    }

    private static SummaryBatch ToSummaryBatch(SummaryBatchFact fact, DateTimeOffset createdAt) =>
        new(
            fact.SummaryBatchId,
            fact.SessionId,
            fact.SettleReason,
            fact.RequestState,
            fact.SourceGraphRevision,
            fact.ModelRequestMetadataJson,
            createdAt,
            [.. fact.Members.Select((member, index) => new SummaryBatchMember(
                index + 1,
                member.TargetKind,
                member.TargetStableId,
                member.TouchOrdinal,
                [.. member.CoTouchedMemberOrdinals]))]);

    private static string ToDatabase(SummaryBatchRequestState state) => state switch
    {
        SummaryBatchRequestState.Pending => "pending",
        SummaryBatchRequestState.Requested => "requested",
        SummaryBatchRequestState.Completed => "completed",
        SummaryBatchRequestState.Degraded => "degraded",
        SummaryBatchRequestState.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown summary batch request state."),
    };

    private static SummaryBatchRequestState FromDatabase(string value) => value switch
    {
        "pending" => SummaryBatchRequestState.Pending,
        "requested" => SummaryBatchRequestState.Requested,
        "completed" => SummaryBatchRequestState.Completed,
        "degraded" => SummaryBatchRequestState.Degraded,
        "skipped" => SummaryBatchRequestState.Skipped,
        _ => throw new InvalidOperationException($"Unknown persisted summary batch request state '{value}'."),
    };

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    [System.Text.Json.Serialization.JsonSerializable(typeof(int[]))]
    private sealed partial class SummaryBatchJsonContext : JsonSerializerContext;
}
