using System.Globalization;
using Microsoft.Data.Sqlite;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Decisions.ArchitectureDecisions;

public sealed class ArchitectureDecisionRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IArchitectureDecisionRepository
{
    public async ValueTask<Result<ArchitectureDecision>> RecordAsync(
        WorkspaceStateLocation location,
        ArchitectureDecisionFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<ArchitectureDecision>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureDecision>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var referenceProblem = await ValidateReferencesAsync(connection, transaction, location.WorkspaceId, fact, cancellationToken);
            if (referenceProblem is not null)
            {
                return ResultFactory.Failure<ArchitectureDecision>(referenceProblem);
            }

            var decisionId = Guid.NewGuid().ToString("N");
            var occurredAt = timeProvider.GetUtcNow();
            await SessionDatabase.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO decisions(decision_id, workspace_id, decision_type, resolution, note, actor_kind, actor_id, session_id, graph_revision, occurred_at_utc) VALUES ($decisionId, $workspaceId, $decisionType, $resolution, $note, $actorKind, $actorId, $sessionId, $graphRevision, $occurredAt);",
                cancellationToken,
                ("$decisionId", decisionId),
                ("$workspaceId", location.WorkspaceId),
                ("$decisionType", fact.DecisionType),
                ("$resolution", ToDatabase(fact.Resolution)),
                ("$note", string.IsNullOrWhiteSpace(fact.Note) ? null : fact.Note),
                ("$actorKind", fact.ActorKind),
                ("$actorId", fact.ActorId),
                ("$sessionId", fact.SessionId),
                ("$graphRevision", fact.GraphRevision),
                ("$occurredAt", ToDatabaseTime(occurredAt)));
            for (var index = 0; index < fact.Targets.Count; index++)
            {
                var target = fact.Targets[index];
                await SessionDatabase.ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO decision_targets(decision_id, target_ordinal, target_kind, target_stable_id) VALUES ($decisionId, $targetOrdinal, $targetKind, $targetStableId);",
                    cancellationToken,
                    ("$decisionId", decisionId),
                    ("$targetOrdinal", index),
                    ("$targetKind", ArchitectureTargetCodec.ToStorageValue(target.Kind)),
                    ("$targetStableId", target.StableId));
            }

            if (fact.SessionId is not null)
            {
                await AppendDecisionEventAsync(
                    connection,
                    transaction,
                    location.WorkspaceId,
                    fact.SessionId,
                    decisionId,
                    fact.GraphRevision,
                    occurredAt,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new ArchitectureDecision(
                decisionId,
                fact.DecisionType,
                fact.Resolution,
                string.IsNullOrWhiteSpace(fact.Note) ? null : fact.Note,
                fact.ActorKind,
                fact.ActorId,
                fact.SessionId,
                fact.GraphRevision,
                [.. fact.Targets],
                occurredAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ArchitectureDecision>(
                Problem.Storage($"Archy could not record the architecture decision: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<ArchitectureDecision>>> ListForTargetAsync(
        WorkspaceStateLocation location,
        ArchitectureTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.StableId))
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureDecision>>(
                Problem.Validation("Decision target identity is required."));
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureDecision>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT d.decision_id, d.decision_type, d.resolution, d.note, d.actor_kind, d.actor_id, d.session_id, d.graph_revision, d.occurred_at_utc, all_targets.target_kind, all_targets.target_stable_id FROM decisions d INNER JOIN decision_targets matched_target ON matched_target.decision_id = d.decision_id INNER JOIN decision_targets all_targets ON all_targets.decision_id = d.decision_id WHERE d.workspace_id = $workspaceId AND matched_target.target_kind = $targetKind AND matched_target.target_stable_id = $targetStableId ORDER BY d.occurred_at_utc, d.decision_id, all_targets.target_ordinal;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$targetKind", ArchitectureTargetCodec.ToStorageValue(target.Kind));
            command.Parameters.AddWithValue("$targetStableId", target.StableId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var decisions = new List<DecisionBuilder>();
            var decisionsById = new Dictionary<string, DecisionBuilder>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                var decisionId = reader.GetString(0);
                if (!decisionsById.TryGetValue(decisionId, out var decision))
                {
                    decision = new DecisionBuilder(
                        decisionId,
                        reader.GetString(1),
                        FromDatabase(reader.GetString(2)),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetInt64(7),
                        FromDatabaseTime(reader.GetString(8)));
                    decisionsById.Add(decisionId, decision);
                    decisions.Add(decision);
                }

                decision.Targets.Add(new ArchitectureTarget(
                    ArchitectureTargetCodec.FromStorageValue(reader.GetString(9)),
                    reader.GetString(10)));
            }

            return ResultFactory.Success<IReadOnlyList<ArchitectureDecision>>(
                [.. decisions.Select(static decision => decision.ToDecision())]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<ArchitectureDecision>>(
                Problem.Storage($"Archy could not read architecture decisions: {exception.Message}"));
        }
    }

    private static Problem? Validate(ArchitectureDecisionFact fact)
    {
        if (string.IsNullOrWhiteSpace(fact.DecisionType) ||
            !Enum.IsDefined(fact.Resolution) ||
            string.IsNullOrWhiteSpace(fact.ActorKind) ||
            string.IsNullOrWhiteSpace(fact.ActorId) ||
            (fact.SessionId is not null && string.IsNullOrWhiteSpace(fact.SessionId)) ||
            fact.Targets is null ||
            fact.Targets.Count == 0 ||
            fact.Targets.Any(static target =>
                target is null ||
                !Enum.IsDefined(target.Kind) ||
                string.IsNullOrWhiteSpace(target.StableId)) ||
            fact.Targets.Select(static target => (target.Kind, target.StableId)).Distinct().Count() != fact.Targets.Count)
        {
            return Problem.Validation("Architecture decisions require type, actor, and distinct target identities.");
        }

        return null;
    }

    private static async Task<Problem?> ValidateReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        ArchitectureDecisionFact fact,
        CancellationToken cancellationToken)
    {
        if (fact.GraphRevision is not null && !await SessionDatabase.ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE workspace_id = $workspaceId AND revision = $revision);",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$revision", fact.GraphRevision)))
        {
            return Problem.Conflict("A decision must reference an existing graph revision in the same workspace.");
        }

        if (fact.SessionId is not null)
        {
            var sessionExists = await SessionDatabase.ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM sessions WHERE workspace_id = $workspaceId AND session_id = $sessionId);",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$sessionId", fact.SessionId));
            if (!sessionExists)
            {
                return Problem.NotFound($"Architecture session '{fact.SessionId}' was not found.");
            }

            var ended = await SessionDatabase.ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM session_events WHERE workspace_id = $workspaceId AND session_id = $sessionId AND event_type = 'session_ended');",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$sessionId", fact.SessionId));
            if (ended)
            {
                return Problem.Conflict($"Architecture session '{fact.SessionId}' has already ended.");
            }
        }

        foreach (var target in fact.Targets)
        {
            (string Table, string KeyColumn)? reference = target.Kind switch
            {
                ArchitectureTargetKind.GraphNode => (Table: "graph_node_identities", KeyColumn: "stable_id"),
                ArchitectureTargetKind.GraphEdge => (Table: "graph_edge_identities", KeyColumn: "edge_id"),
                ArchitectureTargetKind.GraphSymbol => (Table: "symbol_identities", KeyColumn: "symbol_id"),
                _ => null,
            };
            if (reference is null)
            {
                continue;
            }

            var exists = await SessionDatabase.ExistsAsync(
                connection,
                transaction,
                $"SELECT EXISTS(SELECT 1 FROM {reference.Value.Table} WHERE workspace_id = $workspaceId AND {reference.Value.KeyColumn} = $stableId);",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$stableId", target.StableId));
            if (!exists)
            {
                return Problem.Conflict("A graph-targeted decision must reference an identity in the same workspace.");
            }
        }

        return null;
    }

    private static async Task AppendDecisionEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string sessionId,
        string decisionId,
        long? graphRevision,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var nextSequence = await SessionDatabase.ScalarLongAsync(
            connection,
            transaction,
            "SELECT COALESCE(MAX(sequence_number), 0) + 1 FROM session_events WHERE workspace_id = $workspaceId AND session_id = $sessionId;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$sessionId", sessionId));
        await SessionDatabase.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO session_events(event_id, workspace_id, session_id, sequence_number, event_type, graph_revision, target_kind, target_stable_id, decision_id, payload_json, occurred_at_utc) VALUES ($eventId, $workspaceId, $sessionId, $sequenceNumber, 'decision_recorded', $graphRevision, NULL, NULL, $decisionId, $payloadJson, $occurredAt);",
            cancellationToken,
            ("$eventId", Guid.NewGuid().ToString("N")),
            ("$workspaceId", workspaceId),
            ("$sessionId", sessionId),
            ("$sequenceNumber", nextSequence),
            ("$graphRevision", graphRevision),
            ("$decisionId", decisionId),
            ("$payloadJson", "{\"origin\":\"decision_store\"}"),
            ("$occurredAt", ToDatabaseTime(occurredAt)));
    }

    private static string ToDatabase(DecisionResolution resolution) => resolution switch
    {
        DecisionResolution.Accepted => "accepted",
        DecisionResolution.Ignored => "ignored",
        DecisionResolution.Modified => "modified",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown decision resolution."),
    };

    private static DecisionResolution FromDatabase(string value) => value switch
    {
        "accepted" => DecisionResolution.Accepted,
        "ignored" => DecisionResolution.Ignored,
        "modified" => DecisionResolution.Modified,
        _ => throw new InvalidOperationException($"Unknown persisted decision resolution '{value}'."),
    };

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private sealed class DecisionBuilder(
        string decisionId,
        string decisionType,
        DecisionResolution resolution,
        string? note,
        string actorKind,
        string actorId,
        string? sessionId,
        long? graphRevision,
        DateTimeOffset occurredAtUtc)
    {
        internal List<ArchitectureTarget> Targets { get; } = [];

        internal ArchitectureDecision ToDecision() => new(
            decisionId,
            decisionType,
            resolution,
            note,
            actorKind,
            actorId,
            sessionId,
            graphRevision,
            [.. Targets],
            occurredAtUtc);
    }
}
