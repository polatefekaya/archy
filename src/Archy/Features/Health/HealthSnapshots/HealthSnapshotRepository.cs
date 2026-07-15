using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Health.HealthSnapshots;

public sealed class HealthSnapshotRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IHealthSnapshotRepository
{
    public async ValueTask<Result<HealthSnapshot>> RecordAsync(
        WorkspaceStateLocation location,
        HealthSnapshotFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<HealthSnapshot>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<HealthSnapshot>(lease.Problem!);
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
                return ResultFactory.Failure<HealthSnapshot>(referenceProblem);
            }

            var existing = await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM health_snapshots WHERE workspace_id = $workspaceId AND graph_revision = $graphRevision AND calculation_version = $calculationVersion);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$graphRevision", fact.GraphRevision),
                ("$calculationVersion", fact.CalculationVersion));
            if (existing)
            {
                return ResultFactory.Failure<HealthSnapshot>(
                    Problem.Conflict("This immutable health snapshot has already been recorded."));
            }

            var healthSnapshotId = Guid.NewGuid().ToString("N");
            var createdAt = timeProvider.GetUtcNow();
            var components = fact.Components.OrderBy(static component => component.ComponentKey, StringComparer.Ordinal).ToArray();
            var resolutionDecisionIds = fact.ResolutionDecisionIds.OrderBy(static decisionId => decisionId, StringComparer.Ordinal).ToArray();
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO health_snapshots(health_snapshot_id, workspace_id, graph_revision, calculation_version, score, created_at_utc) VALUES ($healthSnapshotId, $workspaceId, $graphRevision, $calculationVersion, $score, $createdAt);",
                cancellationToken,
                ("$healthSnapshotId", healthSnapshotId),
                ("$workspaceId", location.WorkspaceId),
                ("$graphRevision", fact.GraphRevision),
                ("$calculationVersion", fact.CalculationVersion),
                ("$score", fact.Score),
                ("$createdAt", ToDatabaseTime(createdAt)));
            foreach (var component in components)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO health_metric_components(health_snapshot_id, component_key, raw_value, weight, weighted_contribution, detail_json) VALUES ($healthSnapshotId, $componentKey, $rawValue, $weight, $weightedContribution, $detailJson);",
                    cancellationToken,
                    ("$healthSnapshotId", healthSnapshotId),
                    ("$componentKey", component.ComponentKey),
                    ("$rawValue", component.RawValue),
                    ("$weight", component.Weight),
                    ("$weightedContribution", component.WeightedContribution),
                    ("$detailJson", component.DetailJson));
            }

            foreach (var decisionId in resolutionDecisionIds)
            {
                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO health_snapshot_resolution_links(health_snapshot_id, decision_id) VALUES ($healthSnapshotId, $decisionId);",
                    cancellationToken,
                    ("$healthSnapshotId", healthSnapshotId),
                    ("$decisionId", decisionId));
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new HealthSnapshot(
                healthSnapshotId,
                fact.GraphRevision,
                fact.CalculationVersion,
                fact.Score,
                [.. components.Select(static component => new HealthMetricComponent(
                    component.ComponentKey,
                    component.RawValue,
                    component.Weight,
                    component.WeightedContribution,
                    component.DetailJson))],
                resolutionDecisionIds,
                createdAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<HealthSnapshot>(
                Problem.Storage($"Archy could not record the health snapshot: {exception.Message}"));
        }
    }

    public async ValueTask<Result<HealthSnapshot>> GetAsync(
        WorkspaceStateLocation location,
        string healthSnapshotId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(healthSnapshotId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<HealthSnapshot>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var snapshotCommand = connection.CreateCommand();
            snapshotCommand.CommandText = "SELECT graph_revision, calculation_version, score, created_at_utc FROM health_snapshots WHERE workspace_id = $workspaceId AND health_snapshot_id = $healthSnapshotId;";
            snapshotCommand.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            snapshotCommand.Parameters.AddWithValue("$healthSnapshotId", healthSnapshotId);
            await using var snapshotReader = await snapshotCommand.ExecuteReaderAsync(cancellationToken);
            if (!await snapshotReader.ReadAsync(cancellationToken))
            {
                return ResultFactory.Failure<HealthSnapshot>(Problem.NotFound($"Health snapshot '{healthSnapshotId}' was not found."));
            }

            var graphRevision = snapshotReader.GetInt64(0);
            var calculationVersion = snapshotReader.GetString(1);
            var score = snapshotReader.GetDouble(2);
            var createdAt = FromDatabaseTime(snapshotReader.GetString(3));
            await snapshotReader.DisposeAsync();
            var components = await LoadComponentsAsync(connection, healthSnapshotId, cancellationToken);
            var decisionIds = await LoadDecisionIdsAsync(connection, healthSnapshotId, cancellationToken);
            return ResultFactory.Success(new HealthSnapshot(
                healthSnapshotId,
                graphRevision,
                calculationVersion,
                score,
                components,
                decisionIds,
                createdAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<HealthSnapshot>(
                Problem.Storage($"Archy could not read the health snapshot: {exception.Message}"));
        }
    }

    private static Problem? Validate(HealthSnapshotFact fact)
    {
        if (fact.GraphRevision < 1 ||
            string.IsNullOrWhiteSpace(fact.CalculationVersion) ||
            !double.IsFinite(fact.Score) || fact.Score is < 0 or > 100 ||
            fact.Components is null || fact.Components.Count == 0 ||
            fact.Components.Any(static component =>
                component is null ||
                string.IsNullOrWhiteSpace(component.ComponentKey) ||
                !double.IsFinite(component.RawValue) ||
                !double.IsFinite(component.Weight) ||
                !double.IsFinite(component.WeightedContribution) ||
                !IsJson(component.DetailJson)) ||
            fact.Components.Select(static component => component.ComponentKey).Distinct(StringComparer.Ordinal).Count() != fact.Components.Count ||
            fact.ResolutionDecisionIds is null ||
            fact.ResolutionDecisionIds.Any(string.IsNullOrWhiteSpace) ||
            fact.ResolutionDecisionIds.Distinct(StringComparer.Ordinal).Count() != fact.ResolutionDecisionIds.Count)
        {
            return Problem.Validation("Health snapshots require a graph scope, versioned finite score, distinct metric components, and distinct decision references.");
        }

        return null;
    }

    private static async Task<Problem?> ValidateReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        HealthSnapshotFact fact,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE workspace_id = $workspaceId AND revision = $graphRevision);",
                cancellationToken,
                ("$workspaceId", workspaceId),
                ("$graphRevision", fact.GraphRevision)))
        {
            return Problem.Conflict("A health snapshot must reference an existing graph revision in the same workspace.");
        }

        foreach (var decisionId in fact.ResolutionDecisionIds)
        {
            if (!await ExistsAsync(
                    connection,
                    transaction,
                    "SELECT EXISTS(SELECT 1 FROM decisions WHERE workspace_id = $workspaceId AND decision_id = $decisionId);",
                    cancellationToken,
                    ("$workspaceId", workspaceId),
                    ("$decisionId", decisionId)))
            {
                return Problem.Conflict("A health snapshot resolution link must reference a decision in the same workspace.");
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<HealthMetricComponent>> LoadComponentsAsync(
        SqliteConnection connection,
        string healthSnapshotId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT component_key, raw_value, weight, weighted_contribution, detail_json FROM health_metric_components WHERE health_snapshot_id = $healthSnapshotId ORDER BY component_key;";
        command.Parameters.AddWithValue("$healthSnapshotId", healthSnapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var components = new List<HealthMetricComponent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            components.Add(new HealthMetricComponent(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4)));
        }

        return [.. components];
    }

    private static async Task<IReadOnlyList<string>> LoadDecisionIdsAsync(
        SqliteConnection connection,
        string healthSnapshotId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT decision_id FROM health_snapshot_resolution_links WHERE health_snapshot_id = $healthSnapshotId ORDER BY decision_id;";
        command.Parameters.AddWithValue("$healthSnapshotId", healthSnapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var decisionIds = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            decisionIds.Add(reader.GetString(0));
        }

        return [.. decisionIds];
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

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
