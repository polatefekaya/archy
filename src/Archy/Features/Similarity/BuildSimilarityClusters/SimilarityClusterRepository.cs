using System.Globalization;
using System.Text.Json;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Similarity.RetrieveHybridCandidates;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Similarity.BuildSimilarityClusters;

public sealed record PersistedSimilarityClusterMember(string StableId, double Score, IReadOnlyList<SimilarityEvidenceKind> EvidenceKinds);
public sealed record PersistedSimilarityCluster(string Id, string Label, IReadOnlyList<PersistedSimilarityClusterMember> Members);
public sealed record PersistedSimilarityClusterRevision(string Id, long GraphRevision, string Algorithm, string AlgorithmVersion, string InputHash, DateTimeOffset CreatedAtUtc, IReadOnlyList<PersistedSimilarityCluster> Clusters);

public interface ISimilarityClusterRepository
{
    ValueTask<Result<PersistedSimilarityClusterRevision>> RecordAsync(WorkspaceStateLocation location, SimilarityClusterBuildResult result, CancellationToken cancellationToken);
    ValueTask<Result<PersistedSimilarityClusterRevision?>> ReadLatestAsync(WorkspaceStateLocation location, long? graphRevision, CancellationToken cancellationToken);
    ValueTask<Result<PersistedSimilarityCluster?>> ReadClusterAsync(WorkspaceStateLocation location, string clusterId, long? graphRevision, CancellationToken cancellationToken);
}

/// <summary>Persists immutable, graph-revision-scoped similarity clusters without sharing placement-cluster tables.</summary>
public sealed class SimilarityClusterRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : ISimilarityClusterRepository
{
    public async ValueTask<Result<PersistedSimilarityClusterRevision>> RecordAsync(WorkspaceStateLocation location, SimilarityClusterBuildResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(result);
        if (result.GraphRevision < 1 || result.Clusters.Count == 0 || result.Clusters.Any(cluster => cluster.Members.Count < 2)) return ResultFactory.Failure<PersistedSimilarityClusterRevision>(Problem.Validation("Similarity clusters require a graph revision and at least two members per cluster."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<PersistedSimilarityClusterRevision>(lease.Problem!);
        using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (!await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE workspace_id=$workspace AND revision=$revision)", cancellationToken, ("$workspace", location.WorkspaceId), ("$revision", result.GraphRevision))) return ResultFactory.Failure<PersistedSimilarityClusterRevision>(Problem.Conflict("Similarity clusters must reference an existing graph revision in the same workspace."));
            if (await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM similarity_cluster_revisions WHERE workspace_id=$workspace AND graph_revision=$revision AND algorithm=$algorithm AND algorithm_version=$version AND input_hash=$hash)", cancellationToken, ("$workspace", location.WorkspaceId), ("$revision", result.GraphRevision), ("$algorithm", result.Algorithm), ("$version", result.AlgorithmVersion), ("$hash", result.InputHash))) return ResultFactory.Failure<PersistedSimilarityClusterRevision>(Problem.Conflict("This immutable similarity cluster revision already exists."));
            var revisionId = Guid.NewGuid().ToString("N"); var created = timeProvider.GetUtcNow();
            await ExecuteAsync(connection, transaction, "INSERT INTO similarity_cluster_revisions(similarity_cluster_revision_id,workspace_id,graph_revision,algorithm,algorithm_version,input_hash,created_at_utc) VALUES($id,$workspace,$revision,$algorithm,$version,$hash,$created)", cancellationToken, ("$id", revisionId), ("$workspace", location.WorkspaceId), ("$revision", result.GraphRevision), ("$algorithm", result.Algorithm), ("$version", result.AlgorithmVersion), ("$hash", result.InputHash), ("$created", DatabaseTime(created)));
            var persisted = new List<PersistedSimilarityCluster>();
            foreach (var cluster in result.Clusters.OrderBy(cluster => cluster.Label, StringComparer.Ordinal))
            {
                var clusterId = await GetOrCreateClusterIdAsync(connection, transaction, location.WorkspaceId, cluster.Label, created, cancellationToken);
                var members = new List<PersistedSimilarityClusterMember>(); var ordinal = 0;
                foreach (var member in cluster.Members.OrderBy(member => member.StableId, StringComparer.Ordinal))
                {
                    if (!await ExistsAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM graph_nodes WHERE revision=$revision AND stable_id=$stableId)", cancellationToken, ("$revision", result.GraphRevision), ("$stableId", member.StableId))) return ResultFactory.Failure<PersistedSimilarityClusterRevision>(Problem.Conflict("Similarity cluster members must exist in the referenced graph revision."));
                    var kinds = member.EvidenceKinds.Order().ToArray(); var evidence = JsonSerializer.Serialize(kinds, SimilarityClusterJsonContext.Default.SimilarityEvidenceKindArray);
                    await ExecuteAsync(connection, transaction, "INSERT INTO similarity_cluster_members(similarity_cluster_revision_id,similarity_cluster_id,member_stable_id,membership_score,evidence_summary_json,member_ordinal) VALUES($revisionId,$clusterId,$stableId,$score,$evidence,$ordinal)", cancellationToken, ("$revisionId", revisionId), ("$clusterId", clusterId), ("$stableId", member.StableId), ("$score", member.Score), ("$evidence", evidence), ("$ordinal", ordinal++));
                    members.Add(new(member.StableId, member.Score, kinds));
                }
                persisted.Add(new(clusterId, cluster.Label, members));
            }
            await transaction.CommitAsync(cancellationToken); return ResultFactory.Success(new PersistedSimilarityClusterRevision(revisionId, result.GraphRevision, result.Algorithm, result.AlgorithmVersion, result.InputHash, created, persisted));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<PersistedSimilarityClusterRevision>(Problem.Storage($"Archy could not persist similarity clusters: {exception.Message}")); }
    }

    public async ValueTask<Result<PersistedSimilarityClusterRevision?>> ReadLatestAsync(WorkspaceStateLocation location, long? graphRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); if (graphRevision is < 1) return ResultFactory.Failure<PersistedSimilarityClusterRevision?>(Problem.Validation("Graph revision must be positive when supplied."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<PersistedSimilarityClusterRevision?>(lease.Problem!); using var held = lease.Value;
        try { SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = graphRevision is null ? "SELECT similarity_cluster_revision_id,graph_revision,algorithm,algorithm_version,input_hash,created_at_utc FROM similarity_cluster_revisions WHERE workspace_id=$workspace ORDER BY created_at_utc DESC LIMIT 1" : "SELECT similarity_cluster_revision_id,graph_revision,algorithm,algorithm_version,input_hash,created_at_utc FROM similarity_cluster_revisions WHERE workspace_id=$workspace AND graph_revision=$revision ORDER BY created_at_utc DESC LIMIT 1"; command.Parameters.AddWithValue("$workspace", location.WorkspaceId); if (graphRevision is not null) command.Parameters.AddWithValue("$revision", graphRevision.Value); await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return ResultFactory.Success<PersistedSimilarityClusterRevision?>(null); var revision = new PersistedSimilarityClusterRevision(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture), await ReadClustersAsync(connection, reader.GetString(0), cancellationToken)); return ResultFactory.Success<PersistedSimilarityClusterRevision?>(revision); }
        catch (SqliteException exception) { return ResultFactory.Failure<PersistedSimilarityClusterRevision?>(Problem.Storage($"Archy could not read similarity clusters: {exception.Message}")); }
    }

    public async ValueTask<Result<PersistedSimilarityCluster?>> ReadClusterAsync(WorkspaceStateLocation location, string clusterId, long? graphRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentException.ThrowIfNullOrWhiteSpace(clusterId); var revision = await ReadLatestAsync(location, graphRevision, cancellationToken); if (!revision.IsSuccess) return ResultFactory.Failure<PersistedSimilarityCluster?>(revision.Problem!); return ResultFactory.Success(revision.Value?.Clusters.SingleOrDefault(cluster => cluster.Id == clusterId));
    }

    private static async Task<IReadOnlyList<PersistedSimilarityCluster>> ReadClustersAsync(SqliteConnection connection, string revisionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT m.similarity_cluster_id,i.label,m.member_stable_id,m.membership_score,m.evidence_summary_json FROM similarity_cluster_members m JOIN similarity_cluster_identities i ON i.similarity_cluster_id=m.similarity_cluster_id WHERE m.similarity_cluster_revision_id=$revision ORDER BY i.label,m.member_ordinal"; command.Parameters.AddWithValue("$revision", revisionId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var output = new List<(string Id, string Label, List<PersistedSimilarityClusterMember> Members)>(); var byId = new Dictionary<string, int>(StringComparer.Ordinal); while (await reader.ReadAsync(cancellationToken)) { var id = reader.GetString(0); if (!byId.TryGetValue(id, out var index)) { index = output.Count; byId.Add(id, index); output.Add((id, reader.GetString(1), [])); } var kinds = JsonSerializer.Deserialize(reader.GetString(4), SimilarityClusterJsonContext.Default.SimilarityEvidenceKindArray) ?? []; output[index].Members.Add(new(reader.GetString(2), reader.GetDouble(3), kinds)); } return output.Select(cluster => new PersistedSimilarityCluster(cluster.Id, cluster.Label, cluster.Members)).ToArray();
    }

    private static async Task<string> GetOrCreateClusterIdAsync(SqliteConnection connection, SqliteTransaction transaction, string workspaceId, string label, DateTimeOffset created, CancellationToken cancellationToken)
    { await using var find = connection.CreateCommand(); find.Transaction = transaction; find.CommandText = "SELECT similarity_cluster_id FROM similarity_cluster_identities WHERE workspace_id=$workspace AND label=$label"; find.Parameters.AddWithValue("$workspace", workspaceId); find.Parameters.AddWithValue("$label", label); var value = await find.ExecuteScalarAsync(cancellationToken); if (value is string existing) return existing; var id = Guid.NewGuid().ToString("N"); await ExecuteAsync(connection, transaction, "INSERT INTO similarity_cluster_identities(similarity_cluster_id,workspace_id,label,created_at_utc) VALUES($id,$workspace,$label,$created)", cancellationToken, ("$id", id), ("$workspace", workspaceId), ("$label", label), ("$created", DatabaseTime(created))); return id; }
    private static async Task<bool> ExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0; }
    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static string DatabaseTime(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
