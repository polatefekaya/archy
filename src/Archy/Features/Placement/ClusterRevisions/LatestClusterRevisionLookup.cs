using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Placement.ClusterRevisions;

/// <summary>Reads the latest persisted cluster revision without exposing storage concerns to placement callers.</summary>
public sealed class LatestClusterRevisionLookup(IWorkspaceLockManager lockManager)
{
    public async ValueTask<Result<IReadOnlyList<ClusterFact>>> ReadAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken)
    {
        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Read,
            TimeSpan.FromSeconds(30),
            cancellationToken);

        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ClusterFact>>(lease.Problem!);
        }

        using var heldLease = lease.Value;

        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.cluster_key, cm.target_stable_id
                FROM cluster_revisions AS cr
                JOIN cluster_memberships AS cm ON cm.cluster_revision_id = cr.cluster_revision_id
                JOIN clusters AS c ON c.cluster_id = cm.cluster_id
                WHERE cr.workspace_id = $workspaceId
                  AND cr.cluster_revision_id = (
                      SELECT cluster_revision_id
                      FROM cluster_revisions
                      WHERE workspace_id = $workspaceId
                      ORDER BY created_at_utc DESC
                      LIMIT 1)
                ORDER BY c.cluster_key, cm.member_ordinal;
                """;
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var membersByCluster = new Dictionary<string, List<ClusterMemberFact>>(StringComparer.Ordinal);

            while (await reader.ReadAsync(cancellationToken))
            {
                var clusterKey = reader.GetString(0);
                if (!membersByCluster.TryGetValue(clusterKey, out var members))
                {
                    members = [];
                    membersByCluster.Add(clusterKey, members);
                }

                members.Add(new ClusterMemberFact(
                    new ArchitectureTarget(ArchitectureTargetKind.GraphNode, reader.GetString(1)),
                    MembershipWeight: 1));
            }

            var clusters = membersByCluster
                .Select(static pair => new ClusterFact(pair.Key, pair.Value))
                .ToArray();
            return ResultFactory.Success<IReadOnlyList<ClusterFact>>(clusters);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<IReadOnlyList<ClusterFact>>(Problem.Storage(exception.Message));
        }
    }
}
