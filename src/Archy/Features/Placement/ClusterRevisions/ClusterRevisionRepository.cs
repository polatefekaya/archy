using System.Globalization;
using Microsoft.Data.Sqlite;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Placement.ClusterRevisions;

public sealed class ClusterRevisionRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IClusterRevisionRepository
{
    public async ValueTask<Result<ClusterRevision>> RecordAsync(
        WorkspaceStateLocation location,
        ClusterRevisionFact fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(fact);
        var invalid = Validate(fact);
        if (invalid is not null)
        {
            return ResultFactory.Failure<ClusterRevision>(invalid);
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ClusterRevision>(lease.Problem!);
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
                return ResultFactory.Failure<ClusterRevision>(referenceProblem);
            }

            var existing = await ExistsAsync(
                connection,
                transaction,
                "SELECT EXISTS(SELECT 1 FROM cluster_revisions WHERE workspace_id = $workspaceId AND graph_revision = $graphRevision AND algorithm = $algorithm AND algorithm_version = $algorithmVersion AND input_hash = $inputHash);",
                cancellationToken,
                ("$workspaceId", location.WorkspaceId),
                ("$graphRevision", fact.GraphRevision),
                ("$algorithm", fact.Algorithm),
                ("$algorithmVersion", fact.AlgorithmVersion),
                ("$inputHash", fact.InputHash));
            if (existing)
            {
                return ResultFactory.Failure<ClusterRevision>(
                    Problem.Conflict("This immutable cluster revision has already been recorded."));
            }

            var createdAt = timeProvider.GetUtcNow();
            var clusterRevisionId = Guid.NewGuid().ToString("N");
            var orderedClusters = fact.Clusters.OrderBy(static cluster => cluster.ClusterKey, StringComparer.Ordinal).ToArray();
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO cluster_revisions(cluster_revision_id, workspace_id, graph_revision, algorithm, algorithm_version, input_hash, created_at_utc) VALUES ($clusterRevisionId, $workspaceId, $graphRevision, $algorithm, $algorithmVersion, $inputHash, $createdAt);",
                cancellationToken,
                ("$clusterRevisionId", clusterRevisionId),
                ("$workspaceId", location.WorkspaceId),
                ("$graphRevision", fact.GraphRevision),
                ("$algorithm", fact.Algorithm),
                ("$algorithmVersion", fact.AlgorithmVersion),
                ("$inputHash", fact.InputHash),
                ("$createdAt", ToDatabaseTime(createdAt)));

            var clusters = new List<Cluster>(orderedClusters.Length);
            foreach (var clusterFact in orderedClusters)
            {
                var clusterId = await GetOrCreateClusterIdAsync(
                    connection,
                    transaction,
                    location.WorkspaceId,
                    clusterFact.ClusterKey,
                    createdAt,
                    cancellationToken);
                var members = new List<ClusterMember>(clusterFact.Members.Count);
                for (var index = 0; index < clusterFact.Members.Count; index++)
                {
                    var member = clusterFact.Members[index];
                    await ExecuteAsync(
                        connection,
                        transaction,
                        "INSERT INTO cluster_memberships(cluster_revision_id, cluster_id, member_ordinal, target_kind, target_stable_id, membership_weight) VALUES ($clusterRevisionId, $clusterId, $memberOrdinal, $targetKind, $targetStableId, $membershipWeight);",
                        cancellationToken,
                        ("$clusterRevisionId", clusterRevisionId),
                        ("$clusterId", clusterId),
                        ("$memberOrdinal", index),
                        ("$targetKind", ArchitectureTargetCodec.ToStorageValue(member.Target.Kind)),
                        ("$targetStableId", member.Target.StableId),
                        ("$membershipWeight", member.MembershipWeight));
                    members.Add(new ClusterMember(member.Target, member.MembershipWeight));
                }

                clusters.Add(new Cluster(clusterId, clusterFact.ClusterKey, [.. members]));
            }

            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new ClusterRevision(
                clusterRevisionId,
                fact.GraphRevision,
                fact.Algorithm,
                fact.AlgorithmVersion,
                fact.InputHash,
                [.. clusters],
                createdAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ClusterRevision>(
                Problem.Storage($"Archy could not record the cluster revision: {exception.Message}"));
        }
    }

    public async ValueTask<Result<ClusterRevision>> GetAsync(
        WorkspaceStateLocation location,
        string clusterRevisionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterRevisionId);
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<ClusterRevision>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var revisionCommand = connection.CreateCommand();
            revisionCommand.CommandText = "SELECT graph_revision, algorithm, algorithm_version, input_hash, created_at_utc FROM cluster_revisions WHERE workspace_id = $workspaceId AND cluster_revision_id = $clusterRevisionId;";
            revisionCommand.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);
            revisionCommand.Parameters.AddWithValue("$clusterRevisionId", clusterRevisionId);
            await using var revisionReader = await revisionCommand.ExecuteReaderAsync(cancellationToken);
            if (!await revisionReader.ReadAsync(cancellationToken))
            {
                return ResultFactory.Failure<ClusterRevision>(Problem.NotFound($"Cluster revision '{clusterRevisionId}' was not found."));
            }

            var graphRevision = revisionReader.GetInt64(0);
            var algorithm = revisionReader.GetString(1);
            var algorithmVersion = revisionReader.GetString(2);
            var inputHash = revisionReader.GetString(3);
            var createdAt = FromDatabaseTime(revisionReader.GetString(4));
            await revisionReader.DisposeAsync();
            var clusters = await LoadClustersAsync(connection, clusterRevisionId, cancellationToken);
            return ResultFactory.Success(new ClusterRevision(
                clusterRevisionId,
                graphRevision,
                algorithm,
                algorithmVersion,
                inputHash,
                clusters,
                createdAt));
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<ClusterRevision>(
                Problem.Storage($"Archy could not read the cluster revision: {exception.Message}"));
        }
    }

    private static Problem? Validate(ClusterRevisionFact fact)
    {
        if (fact.GraphRevision < 1 ||
            string.IsNullOrWhiteSpace(fact.Algorithm) ||
            string.IsNullOrWhiteSpace(fact.AlgorithmVersion) ||
            string.IsNullOrWhiteSpace(fact.InputHash) ||
            fact.Clusters is null ||
            fact.Clusters.Count == 0 ||
            fact.Clusters.Any(static cluster => cluster is null || string.IsNullOrWhiteSpace(cluster.ClusterKey) || cluster.Members is null || cluster.Members.Count == 0) ||
            fact.Clusters.Select(static cluster => cluster.ClusterKey).Distinct(StringComparer.Ordinal).Count() != fact.Clusters.Count ||
            fact.Clusters.SelectMany(static cluster => cluster.Members).Any(static member =>
                member is null ||
                member.Target is null ||
                member.Target.Kind is not (ArchitectureTargetKind.GraphNode or ArchitectureTargetKind.GraphSymbol) ||
                string.IsNullOrWhiteSpace(member.Target.StableId) ||
                !double.IsFinite(member.MembershipWeight) ||
                member.MembershipWeight < 0) ||
            fact.Clusters.Any(static cluster => cluster.Members.Select(static member => member.Target).Distinct().Count() != cluster.Members.Count) ||
            fact.Clusters.SelectMany(static cluster => cluster.Members).Select(static member => member.Target).Distinct().Count() != fact.Clusters.Sum(static cluster => cluster.Members.Count))
        {
            return Problem.Validation("Cluster revisions require a graph scope, deterministic algorithm identity, and distinct graph-node or graph-symbol members.");
        }

        return null;
    }

    private static async Task<Problem?> ValidateReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        ClusterRevisionFact fact,
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
            return Problem.Conflict("A cluster revision must reference an existing graph revision in the same workspace.");
        }

        foreach (var target in fact.Clusters.SelectMany(static cluster => cluster.Members).Select(static member => member.Target).Distinct())
        {
            var (table, keyColumn) = target.Kind switch
            {
                ArchitectureTargetKind.GraphNode => ("graph_node_identities", "stable_id"),
                ArchitectureTargetKind.GraphSymbol => ("symbol_identities", "symbol_id"),
                _ => throw new InvalidOperationException("Validated cluster target kind was not supported."),
            };
            if (!await ExistsAsync(
                    connection,
                    transaction,
                    $"SELECT EXISTS(SELECT 1 FROM {table} WHERE workspace_id = $workspaceId AND {keyColumn} = $stableId);",
                    cancellationToken,
                    ("$workspaceId", workspaceId),
                    ("$stableId", target.StableId)))
            {
                return Problem.Conflict("A cluster member must exist in the same workspace.");
            }
        }

        return null;
    }

    private static async Task<string> GetOrCreateClusterIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string clusterKey,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var existing = await ScalarStringAsync(
            connection,
            transaction,
            "SELECT cluster_id FROM clusters WHERE workspace_id = $workspaceId AND cluster_key = $clusterKey;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$clusterKey", clusterKey));
        if (existing is not null)
        {
            return existing;
        }

        var clusterId = Guid.NewGuid().ToString("N");
        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO clusters(cluster_id, workspace_id, cluster_key, created_at_utc) VALUES ($clusterId, $workspaceId, $clusterKey, $createdAt);",
            cancellationToken,
            ("$clusterId", clusterId),
            ("$workspaceId", workspaceId),
            ("$clusterKey", clusterKey),
            ("$createdAt", ToDatabaseTime(createdAt)));
        return clusterId;
    }

    private static async Task<IReadOnlyList<Cluster>> LoadClustersAsync(
        SqliteConnection connection,
        string clusterRevisionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.cluster_id, c.cluster_key, m.target_kind, m.target_stable_id, m.membership_weight FROM cluster_memberships m INNER JOIN clusters c ON c.cluster_id = m.cluster_id WHERE m.cluster_revision_id = $clusterRevisionId ORDER BY c.cluster_key, m.member_ordinal;";
        command.Parameters.AddWithValue("$clusterRevisionId", clusterRevisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var clusters = new List<ClusterBuilder>();
        var clustersById = new Dictionary<string, ClusterBuilder>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var clusterId = reader.GetString(0);
            if (!clustersById.TryGetValue(clusterId, out var cluster))
            {
                cluster = new ClusterBuilder(clusterId, reader.GetString(1));
                clustersById.Add(clusterId, cluster);
                clusters.Add(cluster);
            }

            cluster.Members.Add(new ClusterMember(
                new ArchitectureTarget(ArchitectureTargetCodec.FromStorageValue(reader.GetString(2)), reader.GetString(3)),
                reader.GetDouble(4)));
        }

        return [.. clusters.Select(static cluster => cluster.ToCluster())];
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

    private static string ToDatabaseTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromDatabaseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private sealed class ClusterBuilder(string clusterId, string clusterKey)
    {
        internal List<ClusterMember> Members { get; } = [];

        internal Cluster ToCluster() => new(clusterId, clusterKey, [.. Members]);
    }
}
