using Microsoft.Data.Sqlite;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

internal static class GraphRevisionIntegrityValidator
{
    public static async Task<Problem?> ValidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        long revision,
        CancellationToken cancellationToken)
    {
        var revisionWorkspaceId = await GraphSql.ScalarStringAsync(
            connection,
            transaction,
            "SELECT workspace_id FROM graph_revisions WHERE revision = $revision;",
            cancellationToken,
            ("$revision", revision));
        if (!string.Equals(revisionWorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            return Problem.Conflict("A graph revision can only be activated in its owning workspace.");
        }

        var hasDanglingSnapshotEdge = await GraphSql.ScalarLongAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM graph_edges edge LEFT JOIN graph_nodes source ON source.revision = edge.revision AND source.stable_id = edge.source_stable_id LEFT JOIN graph_nodes target ON target.revision = edge.revision AND target.stable_id = edge.target_stable_id WHERE edge.revision = $revision AND (source.stable_id IS NULL OR target.stable_id IS NULL));",
            cancellationToken,
            ("$revision", revision));
        if (hasDanglingSnapshotEdge == 1)
        {
            return Problem.Conflict("A graph revision with dangling snapshot edges cannot become active.");
        }

        return null;
    }
}
