using System.Globalization;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Sessions.ReplayEventPages;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ArchitectureSessions;

public sealed class ArchitectureSessionRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IArchitectureSessionRepository, IArchitectureSessionEventReplayReader
{
    public async ValueTask<Result<ArchitectureSession>> StartAsync(
        WorkspaceStateLocation location,
        SessionStartFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = ValidateStart(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<ArchitectureSession>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureSession>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var identityProblem = await ValidateStartIdentityAsync(
                connection,
                transaction,
                location.WorkspaceId,
                fact,
                cancellationToken);
            if (identityProblem is not null)
            {
                return ResultFactory.Failure<ArchitectureSession>(identityProblem);
            }

            var startedAt = timeProvider.GetUtcNow();
            await SessionDatabase.ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO sessions(session_id, workspace_id, client_kind, external_session_id, actor_kind, actor_id, started_at_utc) VALUES ($sessionId, $workspaceId, $clientKind, $externalSessionId, $actorKind, $actorId, $startedAt);",
                cancellationToken,
                ("$sessionId", fact.SessionId),
                ("$workspaceId", location.WorkspaceId),
                ("$clientKind", fact.ClientKind),
                ("$externalSessionId", fact.ExternalSessionId),
                ("$actorKind", fact.ActorKind),
                ("$actorId", fact.ActorId),
                ("$startedAt", ToDatabaseTime(startedAt)));
            _ = await AppendEventCoreAsync(
                connection,
                transaction,
                location.WorkspaceId,
                fact.SessionId,
                new SessionEventFact(SessionEventKind.SessionStarted, null, null, fact.StartPayloadJson),
                decisionId: null,
                startedAt,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ResultFactory.Success(new ArchitectureSession(
                fact.SessionId,
                fact.ClientKind,
                fact.ExternalSessionId,
                fact.ActorKind,
                fact.ActorId,
                startedAt,
                EndedAtUtc: null));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ArchitectureSession>(
                Problem.Storage($"Archy could not start the architecture session: {exception.Message}"));
        }
    }

    public async ValueTask<Result<SessionEvent>> AppendEventAsync(
        WorkspaceStateLocation location,
        string sessionId,
        SessionEventFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = ValidateAppend(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<SessionEvent>(invalid);
        }

        return await AppendWithLeaseAsync(location, sessionId, fact, cancellationToken);
    }

    public async ValueTask<Result<SessionEvent>> EndAsync(
        WorkspaceStateLocation location,
        string sessionId,
        string endPayloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!IsJson(endPayloadJson))
        {
            return ResultFactory.Failure<SessionEvent>(Problem.Validation("Session event payload must be valid JSON."));
        }

        return await AppendWithLeaseAsync(
            location,
            sessionId,
            new SessionEventFact(SessionEventKind.SessionEnded, null, null, endPayloadJson),
            cancellationToken);
    }

    public async ValueTask<Result<ArchitectureSession>> GetAsync(
        WorkspaceStateLocation location,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureSession>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT s.client_kind, s.external_session_id, s.actor_kind, s.actor_id, s.started_at_utc, (SELECT e.occurred_at_utc FROM session_events e WHERE e.workspace_id = s.workspace_id AND e.session_id = s.session_id AND e.event_type = 'session_ended' ORDER BY e.sequence_number DESC LIMIT 1) FROM sessions s WHERE s.workspace_id = $workspaceId AND s.session_id = $sessionId;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$sessionId", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return ResultFactory.Failure<ArchitectureSession>(Problem.NotFound($"Architecture session '{sessionId}' was not found."));
            }

            return ResultFactory.Success(new ArchitectureSession(
                sessionId,
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                FromDatabaseTime(reader.GetString(4)),
                reader.IsDBNull(5) ? null : FromDatabaseTime(reader.GetString(5))));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ArchitectureSession>(
                Problem.Storage($"Archy could not read the architecture session: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<SessionEvent>>> ListEventsAsync(
        WorkspaceStateLocation location,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<SessionEvent>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using (var existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM sessions WHERE workspace_id = $workspaceId AND session_id = $sessionId);";
                existsCommand.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
                existsCommand.Parameters.AddWithValue("$sessionId", sessionId);
                var exists = Convert.ToInt64(
                    await existsCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture) == 1;
                if (!exists)
                {
                    return ResultFactory.Failure<IReadOnlyList<SessionEvent>>(
                        Problem.NotFound($"Architecture session '{sessionId}' was not found."));
                }
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT event_id, sequence_number, event_type, graph_revision, target_kind, target_stable_id, decision_id, payload_json, occurred_at_utc FROM session_events WHERE workspace_id = $workspaceId AND session_id = $sessionId ORDER BY sequence_number;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$sessionId", sessionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var events = new List<SessionEvent>();
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new SessionEvent(
                    reader.GetString(0),
                    sessionId,
                    reader.GetInt32(1),
                    SessionDatabase.EventKindFromDatabase(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(4)), reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    FromDatabaseTime(reader.GetString(8))));
            }

            return ResultFactory.Success<IReadOnlyList<SessionEvent>>([.. events]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<SessionEvent>>(
                Problem.Storage($"Archy could not read session events: {exception.Message}"));
        }
    }

    public async ValueTask<Result<SessionEventReplayPage>> ReadAsync(
        WorkspaceStateLocation location,
        string sessionId,
        int afterSequence,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (afterSequence < 0 || maximumEvents is < 1 or > 1_000)
        {
            return ResultFactory.Failure<SessionEventReplayPage>(Problem.Validation("Session event replay requires a non-negative cursor and a page size between 1 and 1000."));
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SessionEventReplayPage>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using (var existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM sessions WHERE workspace_id = $workspaceId AND session_id = $sessionId);";
                existsCommand.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
                existsCommand.Parameters.AddWithValue("$sessionId", sessionId);
                if (Convert.ToInt64(await existsCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
                {
                    return ResultFactory.Failure<SessionEventReplayPage>(Problem.NotFound($"Architecture session '{sessionId}' was not found."));
                }
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT event_id, sequence_number, event_type, graph_revision, target_kind, target_stable_id, decision_id, payload_json, occurred_at_utc FROM session_events WHERE workspace_id = $workspaceId AND session_id = $sessionId AND sequence_number > $afterSequence ORDER BY sequence_number LIMIT $limit;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$sessionId", sessionId);
            command.Parameters.AddWithValue("$afterSequence", afterSequence);
            command.Parameters.AddWithValue("$limit", maximumEvents + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var events = new List<SessionEvent>();
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new SessionEvent(
                    reader.GetString(0),
                    sessionId,
                    reader.GetInt32(1),
                    SessionDatabase.EventKindFromDatabase(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(4)), reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    FromDatabaseTime(reader.GetString(8))));
            }

            var hasMore = events.Count > maximumEvents;
            if (hasMore) events.RemoveAt(events.Count - 1);
            return ResultFactory.Success(new SessionEventReplayPage([.. events], hasMore));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SessionEventReplayPage>(Problem.Storage($"Archy could not read the session event replay page: {exception.Message}"));
        }
    }

    private async ValueTask<Result<SessionEvent>> AppendWithLeaseAsync(
        WorkspaceStateLocation location,
        string sessionId,
        SessionEventFact fact,
        CancellationToken cancellationToken)
    {
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SessionEvent>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var sessionProblem = await ValidateOpenSessionAsync(connection, transaction, location.WorkspaceId, sessionId, cancellationToken);
            if (sessionProblem is not null)
            {
                return ResultFactory.Failure<SessionEvent>(sessionProblem);
            }

            var factProblem = await ValidateReferencesAsync(connection, transaction, location.WorkspaceId, fact, cancellationToken);
            if (factProblem is not null)
            {
                return ResultFactory.Failure<SessionEvent>(factProblem);
            }

            var appended = await AppendEventCoreAsync(
                connection,
                transaction,
                location.WorkspaceId,
                sessionId,
                fact,
                decisionId: null,
                timeProvider.GetUtcNow(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(appended);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<SessionEvent>(
                Problem.Storage($"Archy could not append the session event: {exception.Message}"));
        }
    }

    private static async Task<SessionEvent> AppendEventCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string sessionId,
        SessionEventFact fact,
        string? decisionId,
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
        var eventId = Guid.NewGuid().ToString("N");
        await SessionDatabase.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO session_events(event_id, workspace_id, session_id, sequence_number, event_type, graph_revision, target_kind, target_stable_id, decision_id, payload_json, occurred_at_utc) VALUES ($eventId, $workspaceId, $sessionId, $sequenceNumber, $eventType, $graphRevision, $targetKind, $targetStableId, $decisionId, $payloadJson, $occurredAt);",
            cancellationToken,
            ("$eventId", eventId),
            ("$workspaceId", workspaceId),
            ("$sessionId", sessionId),
            ("$sequenceNumber", nextSequence),
            ("$eventType", SessionDatabase.ToDatabase(fact.Kind)),
            ("$graphRevision", fact.GraphRevision),
            ("$targetKind", fact.Target is null ? null : ArchitectureTargetCodec.ToStorageValue(fact.Target.Kind)),
            ("$targetStableId", fact.Target?.StableId),
            ("$decisionId", decisionId),
            ("$payloadJson", fact.PayloadJson),
            ("$occurredAt", ToDatabaseTime(occurredAt)));
        return new SessionEvent(
            eventId,
            sessionId,
            checked((int)nextSequence),
            fact.Kind,
            fact.GraphRevision,
            fact.Target,
            decisionId,
            fact.PayloadJson,
            occurredAt);
    }

    private static Problem? ValidateStart(SessionStartFact fact)
    {
        if (string.IsNullOrWhiteSpace(fact.SessionId) ||
            string.IsNullOrWhiteSpace(fact.ClientKind) ||
            (fact.ExternalSessionId is not null && string.IsNullOrWhiteSpace(fact.ExternalSessionId)) ||
            string.IsNullOrWhiteSpace(fact.ActorKind) ||
            string.IsNullOrWhiteSpace(fact.ActorId) ||
            !IsJson(fact.StartPayloadJson))
        {
            return Problem.Validation("Architecture sessions require identity, client, actor, and a valid JSON start payload.");
        }

        return null;
    }

    private static Problem? ValidateAppend(SessionEventFact fact)
    {
        if (!Enum.IsDefined(fact.Kind) ||
            fact.Kind is SessionEventKind.SessionStarted or SessionEventKind.SessionEnded or SessionEventKind.DecisionRecorded)
        {
            return Problem.Validation("Use the dedicated lifecycle or decision operation for this session event kind.");
        }

        if ((fact.Target is not null &&
             (!Enum.IsDefined(fact.Target.Kind) || string.IsNullOrWhiteSpace(fact.Target.StableId))) ||
            !IsJson(fact.PayloadJson))
        {
            return Problem.Validation("Session events require valid target identity, when supplied, and a valid JSON payload.");
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
            SessionDatabase.ValidateJson(value);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static async Task<Problem?> ValidateOpenSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var exists = await SessionDatabase.ExistsAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM sessions WHERE workspace_id = $workspaceId AND session_id = $sessionId);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$sessionId", sessionId));
        if (!exists)
        {
            return Problem.NotFound($"Architecture session '{sessionId}' was not found.");
        }

        var ended = await SessionDatabase.ExistsAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM session_events WHERE workspace_id = $workspaceId AND session_id = $sessionId AND event_type = 'session_ended');",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$sessionId", sessionId));
        return ended ? Problem.Conflict($"Architecture session '{sessionId}' has already ended.") : null;
    }

    private static async Task<Problem?> ValidateStartIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        SessionStartFact fact,
        CancellationToken cancellationToken)
    {
        var sessionExists = await SessionDatabase.ExistsAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM sessions WHERE session_id = $sessionId);",
            cancellationToken,
            ("$sessionId", fact.SessionId));
        if (sessionExists)
        {
            return Problem.Conflict($"Architecture session '{fact.SessionId}' already exists.");
        }

        if (string.IsNullOrWhiteSpace(fact.ExternalSessionId))
        {
            return null;
        }

        var externalIdentityExists = await SessionDatabase.ExistsAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM sessions WHERE workspace_id = $workspaceId AND client_kind = $clientKind AND external_session_id = $externalSessionId);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$clientKind", fact.ClientKind),
            ("$externalSessionId", fact.ExternalSessionId));
        return externalIdentityExists
            ? Problem.Conflict($"An architecture session already exists for external identity '{fact.ExternalSessionId}'.")
            : null;
    }

    private static async Task<Problem?> ValidateReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        SessionEventFact fact,
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
            return Problem.Conflict("A session event must reference an existing graph revision in the same workspace.");
        }

        if (fact.Target is null)
        {
            return null;
        }

        (string Table, string KeyColumn)? reference = fact.Target.Kind switch
        {
            ArchitectureTargetKind.GraphNode => (Table: "graph_node_identities", KeyColumn: "stable_id"),
            ArchitectureTargetKind.GraphEdge => (Table: "graph_edge_identities", KeyColumn: "edge_id"),
            ArchitectureTargetKind.GraphSymbol => (Table: "symbol_identities", KeyColumn: "symbol_id"),
            _ => null,
        };
        if (reference is null)
        {
            return null;
        }

        var exists = await SessionDatabase.ExistsAsync(
            connection,
            transaction,
            $"SELECT EXISTS(SELECT 1 FROM {reference.Value.Table} WHERE workspace_id = $workspaceId AND {reference.Value.KeyColumn} = $stableId);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$stableId", fact.Target.StableId));
        return exists ? null : Problem.Conflict("A graph-targeted session event must reference an identity in the same workspace.");
    }

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
