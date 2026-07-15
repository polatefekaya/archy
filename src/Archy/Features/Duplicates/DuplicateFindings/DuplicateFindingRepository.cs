using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed class DuplicateFindingRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IDuplicateFindingRepository
{
    public async ValueTask<Result<DuplicateFindingObservation>> RecordObservationAsync(
        WorkspaceStateLocation location,
        DuplicateFindingObservationFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<DuplicateFindingObservation>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<DuplicateFindingObservation>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var normalized = NormalizePair(fact.LeftTarget, fact.RightTarget);
            var referenceProblem = await ValidateReferencesAsync(
                connection,
                transaction,
                location.WorkspaceId,
                normalized.Left,
                normalized.Right,
                fact.GraphRevision,
                cancellationToken);
            if (referenceProblem is not null)
            {
                return ResultFactory.Failure<DuplicateFindingObservation>(referenceProblem);
            }

            var findingId = await ScalarStringAsync(
                connection,
                transaction,
                "SELECT finding_id FROM duplicate_finding_identities WHERE workspace_id = $workspaceId AND canonical_pair_key = $canonicalPairKey;",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$canonicalPairKey", normalized.CanonicalPairKey));
            var observedAt = timeProvider.GetUtcNow();
            if (findingId is null)
            {
                findingId = Guid.NewGuid().ToString("N");
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO duplicate_finding_identities(finding_id, workspace_id, canonical_pair_key, left_target_kind, left_target_stable_id, right_target_kind, right_target_stable_id, first_seen_graph_revision, created_at_utc) VALUES ($findingId, $workspaceId, $canonicalPairKey, $leftTargetKind, $leftTargetStableId, $rightTargetKind, $rightTargetStableId, $firstSeenGraphRevision, $createdAt);",
                    cancellationToken,
                    ("$findingId", findingId),
                    ("$workspaceId", location.WorkspaceId),
                    ("$canonicalPairKey", normalized.CanonicalPairKey),
                    ("$leftTargetKind", ArchitectureTargetCodec.ToStorageValue(normalized.Left.Kind)),
                    ("$leftTargetStableId", normalized.Left.StableId),
                    ("$rightTargetKind", ArchitectureTargetCodec.ToStorageValue(normalized.Right.Kind)),
                    ("$rightTargetStableId", normalized.Right.StableId),
                    ("$firstSeenGraphRevision", fact.GraphRevision),
                    ("$createdAt", ToDatabaseTime(observedAt)));
            }

            var observationExists = await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM duplicate_finding_observations WHERE workspace_id = $workspaceId AND finding_id = $findingId AND graph_revision = $graphRevision AND aggregation_version = $aggregationVersion);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$findingId", findingId),
                ("$graphRevision", fact.GraphRevision),
                ("$aggregationVersion", fact.AggregationVersion));
            if (observationExists)
            {
                return ResultFactory.Failure<DuplicateFindingObservation>(
                    Problem.Conflict("This immutable duplicate observation has already been recorded."));
            }

            var findingObservationId = Guid.NewGuid().ToString("N");
            var orderedSignals = fact.Signals.OrderBy(static signal => ToDatabase(signal.Kind), StringComparer.Ordinal).ToArray();
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO duplicate_finding_observations(finding_observation_id, workspace_id, finding_id, graph_revision, aggregation_version, confidence, rationale_json, observed_at_utc) VALUES ($findingObservationId, $workspaceId, $findingId, $graphRevision, $aggregationVersion, $confidence, $rationaleJson, $observedAt);",
                cancellationToken,
                ("$findingObservationId", findingObservationId),
                ("$workspaceId", location.WorkspaceId),
                ("$findingId", findingId),
                ("$graphRevision", fact.GraphRevision),
                ("$aggregationVersion", fact.AggregationVersion),
                ("$confidence", fact.Confidence),
                ("$rationaleJson", fact.RationaleJson),
                ("$observedAt", ToDatabaseTime(observedAt)));
            var signals = new List<DuplicateSignalObservation>(orderedSignals.Length);
            foreach (var signal in orderedSignals)
            {
                var signalObservationId = Guid.NewGuid().ToString("N");
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO duplicate_signal_observations(signal_observation_id, finding_observation_id, signal_kind, score, evidence_json, observed_at_utc) VALUES ($signalObservationId, $findingObservationId, $signalKind, $score, $evidenceJson, $observedAt);",
                    cancellationToken,
                    ("$signalObservationId", signalObservationId),
                    ("$findingObservationId", findingObservationId),
                    ("$signalKind", ToDatabase(signal.Kind)),
                    ("$score", signal.Score),
                    ("$evidenceJson", signal.EvidenceJson),
                    ("$observedAt", ToDatabaseTime(observedAt)));
                signals.Add(new DuplicateSignalObservation(
                    signalObservationId,
                    signal.Kind,
                    signal.Score,
                    signal.EvidenceJson,
                    observedAt));
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new DuplicateFindingObservation(
                findingId,
                findingObservationId,
                normalized.Left,
                normalized.Right,
                fact.GraphRevision,
                fact.AggregationVersion,
                fact.Confidence,
                fact.RationaleJson,
                [.. signals],
                observedAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<DuplicateFindingObservation>(
                Problem.Storage($"Archy could not record the duplicate finding observation: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<DuplicateFindingObservation>>> ListObservationsAsync(
        WorkspaceStateLocation location,
        string findingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<DuplicateFindingObservation>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var identity = await LoadIdentityAsync(connection, location.WorkspaceId, findingId, cancellationToken);
            if (identity is null)
            {
                return ResultFactory.Failure<IReadOnlyList<DuplicateFindingObservation>>(
                    Problem.NotFound($"Duplicate finding '{findingId}' was not found."));
            }

            var observations = new List<ObservationRow>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT finding_observation_id, graph_revision, aggregation_version, confidence, rationale_json, observed_at_utc FROM duplicate_finding_observations WHERE workspace_id = $workspaceId AND finding_id = $findingId ORDER BY graph_revision, observed_at_utc, finding_observation_id;";
                command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
                command.Parameters.AddWithValue("$findingId", findingId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    observations.Add(new ObservationRow(
                        reader.GetString(0),
                        reader.GetInt64(1),
                        reader.GetString(2),
                        reader.GetDouble(3),
                        reader.GetString(4),
                        FromDatabaseTime(reader.GetString(5))));
                }
            }

            var result = new List<DuplicateFindingObservation>(observations.Count);
            foreach (var observation in observations)
            {
                var signals = await LoadSignalsAsync(connection, observation.FindingObservationId, cancellationToken);
                result.Add(new DuplicateFindingObservation(
                    findingId,
                    observation.FindingObservationId,
                    identity.Left,
                    identity.Right,
                    observation.GraphRevision,
                    observation.AggregationVersion,
                    observation.Confidence,
                    observation.RationaleJson,
                    signals,
                    observation.ObservedAtUtc));
            }

            return ResultFactory.Success<IReadOnlyList<DuplicateFindingObservation>>([.. result]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<DuplicateFindingObservation>>(
                Problem.Storage($"Archy could not read duplicate finding observations: {exception.Message}"));
        }
    }

    public async ValueTask<Result<DuplicateResolutionLink>> LinkResolutionAsync(
        WorkspaceStateLocation location,
        string findingId,
        string decisionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<DuplicateResolutionLink>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var findingExists = await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM duplicate_finding_identities WHERE workspace_id = $workspaceId AND finding_id = $findingId);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$findingId", findingId));
            if (!findingExists)
            {
                return ResultFactory.Failure<DuplicateResolutionLink>(Problem.NotFound($"Duplicate finding '{findingId}' was not found."));
            }

            var decisionTargetsFinding = await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM decisions d INNER JOIN decision_targets t ON t.decision_id = d.decision_id WHERE d.workspace_id = $workspaceId AND d.decision_id = $decisionId AND t.target_kind = 'duplicate_finding' AND t.target_stable_id = $findingId);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$decisionId", decisionId),
                ("$findingId", findingId));
            if (!decisionTargetsFinding)
            {
                return ResultFactory.Failure<DuplicateResolutionLink>(
                    Problem.Conflict("A duplicate resolution link requires a same-workspace decision targeted at that finding."));
            }

            var existing = await ScalarStringAsync(
                connection,
                transaction,
                "SELECT linked_at_utc FROM duplicate_finding_resolution_links WHERE finding_id = $findingId AND decision_id = $decisionId;",
                cancellationToken,
                ("$findingId", findingId),
                ("$decisionId", decisionId));
            if (existing is not null)
            {
                return ResultFactory.Success(new DuplicateResolutionLink(findingId, decisionId, FromDatabaseTime(existing)));
            }

            var linkedAt = timeProvider.GetUtcNow();
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO duplicate_finding_resolution_links(finding_id, decision_id, linked_at_utc) VALUES ($findingId, $decisionId, $linkedAt);",
                cancellationToken,
                ("$findingId", findingId),
                ("$decisionId", decisionId),
                ("$linkedAt", ToDatabaseTime(linkedAt)));
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new DuplicateResolutionLink(findingId, decisionId, linkedAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<DuplicateResolutionLink>(
                Problem.Storage($"Archy could not link the duplicate resolution: {exception.Message}"));
        }
    }

    public async ValueTask<Result<IReadOnlyList<DuplicateResolutionLink>>> ListResolutionLinksAsync(
        WorkspaceStateLocation location,
        string findingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<DuplicateResolutionLink>>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT l.decision_id, l.linked_at_utc FROM duplicate_finding_resolution_links l INNER JOIN duplicate_finding_identities f ON f.finding_id = l.finding_id WHERE f.workspace_id = $workspaceId AND l.finding_id = $findingId ORDER BY l.linked_at_utc, l.decision_id;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            command.Parameters.AddWithValue("$findingId", findingId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var links = new List<DuplicateResolutionLink>();
            while (await reader.ReadAsync(cancellationToken))
            {
                links.Add(new DuplicateResolutionLink(findingId, reader.GetString(0), FromDatabaseTime(reader.GetString(1))));
            }

            return ResultFactory.Success<IReadOnlyList<DuplicateResolutionLink>>([.. links]);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<DuplicateResolutionLink>>(
                Problem.Storage($"Archy could not read duplicate resolution links: {exception.Message}"));
        }
    }

    private static Problem? Validate(DuplicateFindingObservationFact fact)
    {
        if (fact.LeftTarget is null || fact.RightTarget is null ||
            !IsCandidateTarget(fact.LeftTarget) || !IsCandidateTarget(fact.RightTarget) ||
            string.IsNullOrWhiteSpace(fact.LeftTarget.StableId) ||
            string.IsNullOrWhiteSpace(fact.RightTarget.StableId) ||
            fact.LeftTarget == fact.RightTarget ||
            fact.GraphRevision < 1 ||
            string.IsNullOrWhiteSpace(fact.AggregationVersion) ||
            !IsProbability(fact.Confidence) ||
            !IsJson(fact.RationaleJson) ||
            fact.Signals is null || fact.Signals.Count == 0 ||
            fact.Signals.Any(static signal =>
                signal is null ||
                !Enum.IsDefined(signal.Kind) ||
                !IsProbability(signal.Score) ||
                !IsJson(signal.EvidenceJson)) ||
            fact.Signals.Select(static signal => signal.Kind).Distinct().Count() != fact.Signals.Count)
        {
            return Problem.Validation("Duplicate observations require two distinct graph targets, a source revision, valid evidence, and distinct probability-scored signals.");
        }

        return null;
    }

    private static bool IsCandidateTarget(ArchitectureTarget target) =>
        target.Kind is ArchitectureTargetKind.GraphNode or ArchitectureTargetKind.GraphSymbol;

    private static bool IsProbability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

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

    private static async Task<Problem?> ValidateReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        ArchitectureTarget left,
        ArchitectureTarget right,
        long graphRevision,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE workspace_id = $workspaceId AND revision = $graphRevision);",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$graphRevision", graphRevision)))
        {
            return Problem.Conflict("A duplicate observation must reference an existing graph revision in the same workspace.");
        }

        foreach (var target in new[] { left, right })
        {
            var (table, keyColumn) = target.Kind switch
            {
                ArchitectureTargetKind.GraphNode => ("graph_node_identities", "stable_id"),
                ArchitectureTargetKind.GraphSymbol => ("symbol_identities", "symbol_id"),
                _ => throw new InvalidOperationException("Validated duplicate target kind was not supported."),
            };
            if (!await ExistsAsync(
                    connection,
                    transaction,
                    $"SELECT EXISTS(SELECT 1 FROM {table} WHERE workspace_id = $workspaceId AND {keyColumn} = $stableId);",
                    cancellationToken,
                    ("$workspaceId", workspaceId),
                    ("$stableId", target.StableId)))
            {
                return Problem.Conflict("A duplicate observation target must exist in the same workspace.");
            }
        }

        return null;
    }

    private static (ArchitectureTarget Left, ArchitectureTarget Right, string CanonicalPairKey) NormalizePair(
        ArchitectureTarget first,
        ArchitectureTarget second)
    {
        var firstKey = $"{ArchitectureTargetCodec.ToStorageValue(first.Kind)}:{first.StableId}";
        var secondKey = $"{ArchitectureTargetCodec.ToStorageValue(second.Kind)}:{second.StableId}";
        return string.CompareOrdinal(firstKey, secondKey) <= 0
            ? (first, second, $"{firstKey}|{secondKey}")
            : (second, first, $"{secondKey}|{firstKey}");
    }

    private static async Task<FindingIdentity?> LoadIdentityAsync(
        SqliteConnection connection,
        string workspaceId,
        string findingId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT left_target_kind, left_target_stable_id, right_target_kind, right_target_stable_id FROM duplicate_finding_identities WHERE workspace_id = $workspaceId AND finding_id = $findingId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$findingId", findingId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FindingIdentity(
                new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(0)), reader.GetString(1)),
                new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(2)), reader.GetString(3)))
            : null;
    }

    private static async Task<IReadOnlyList<DuplicateSignalObservation>> LoadSignalsAsync(
        SqliteConnection connection,
        string findingObservationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT signal_observation_id, signal_kind, score, evidence_json, observed_at_utc FROM duplicate_signal_observations WHERE finding_observation_id = $findingObservationId ORDER BY signal_kind;";
        command.Parameters.AddWithValue("$findingObservationId", findingObservationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var signals = new List<DuplicateSignalObservation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            signals.Add(new DuplicateSignalObservation(
                reader.GetString(0),
                SignalKindFromDatabase(reader.GetString(1)),
                reader.GetDouble(2),
                reader.GetString(3),
                FromDatabaseTime(reader.GetString(4))));
        }

        return [.. signals];
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
        AddParameters(command, parameters);
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
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
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
    }

    private static string ToDatabase(DuplicateSignalKind kind) => kind switch
    {
        DuplicateSignalKind.Structural => "structural",
        DuplicateSignalKind.Signature => "signature",
        DuplicateSignalKind.Semantic => "semantic",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown duplicate signal kind."),
    };

    private static DuplicateSignalKind SignalKindFromDatabase(string value) => value switch
    {
        "structural" => DuplicateSignalKind.Structural,
        "signature" => DuplicateSignalKind.Signature,
        "semantic" => DuplicateSignalKind.Semantic,
        _ => throw new InvalidOperationException($"Unknown persisted duplicate signal kind '{value}'."),
    };

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private sealed record FindingIdentity(ArchitectureTarget Left, ArchitectureTarget Right);

    private sealed record ObservationRow(
        string FindingObservationId,
        long GraphRevision,
        string AggregationVersion,
        double Confidence,
        string RationaleJson,
        DateTimeOffset ObservedAtUtc);
}
