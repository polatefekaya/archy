using Microsoft.Data.Sqlite;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

internal sealed class GraphVersioningWriter(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string workspaceId,
    string runId,
    long revision)
{
    public async Task<Problem?> PersistAsync(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<GraphSymbolFact> symbols,
        IReadOnlyList<InterfaceFingerprintFact> interfaceFingerprints,
        CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            await PersistNodeSnapshotAsync(node, cancellationToken);
            await UpsertNodeIdentityAsync(node.StableId, cancellationToken);
            var activeVersion = await LoadActiveNodeVersionAsync(node.StableId, cancellationToken);
            if (activeVersion is null)
            {
                await InsertNodeVersionAsync(node, cancellationToken);
            }
            else if (!activeVersion.Matches(node))
            {
                await CloseNodeVersionAsync(node.StableId, cancellationToken);
                await InsertNodeVersionAsync(node, cancellationToken);
            }
        }

        await CloseMissingNodeVersionsAsync(
            nodes.Select(node => node.StableId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);

        var symbolProblem = await new SymbolVersioningWriter(
            connection,
            transaction,
            workspaceId,
            runId,
            revision).PersistAsync(symbols, interfaceFingerprints, cancellationToken);
        if (symbolProblem is not null)
        {
            return symbolProblem;
        }

        foreach (var edge in edges)
        {
            await PersistEdgeSnapshotAsync(edge, cancellationToken);
            var identityProblem = await UpsertEdgeIdentityAsync(edge, cancellationToken);
            if (identityProblem is not null)
            {
                return identityProblem;
            }

            var activeVersion = await LoadActiveEdgeVersionAsync(edge.EdgeId, cancellationToken);
            if (activeVersion is null)
            {
                await InsertEdgeVersionAsync(edge, cancellationToken);
            }
            else if (!activeVersion.Matches(edge))
            {
                await CloseEdgeVersionAsync(edge.EdgeId, cancellationToken);
                await InsertEdgeVersionAsync(edge, cancellationToken);
            }
        }

        await CloseMissingEdgeVersionsAsync(
            edges.Select(edge => edge.EdgeId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);

        return null;
    }

    private Task PersistNodeSnapshotAsync(GraphNodeFact node, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_nodes(revision, stable_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash) VALUES ($revision, $stableId, $nodeKind, $canonicalKey, $displayName, $filePath, $startLine, $endLine, $provider, $confidence, $evidenceJson, $contentHash);",
            cancellationToken,
            ("$revision", revision),
            ("$stableId", node.StableId),
            ("$nodeKind", node.NodeKind),
            ("$canonicalKey", node.CanonicalKey),
            ("$displayName", node.DisplayName),
            ("$filePath", node.FilePath),
            ("$startLine", node.StartLine),
            ("$endLine", node.EndLine),
            ("$provider", node.Provider),
            ("$confidence", node.Confidence),
            ("$evidenceJson", node.EvidenceJson),
            ("$contentHash", node.ContentHash));

    private Task UpsertNodeIdentityAsync(string stableId, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_node_identities(workspace_id, stable_id, first_seen_revision, last_seen_revision) VALUES ($workspaceId, $stableId, $revision, $revision) ON CONFLICT(workspace_id, stable_id) DO UPDATE SET last_seen_revision = excluded.last_seen_revision;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$stableId", stableId),
            ("$revision", revision));

    private Task InsertNodeVersionAsync(GraphNodeFact node, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_node_versions(workspace_id, stable_id, valid_from_revision, valid_to_revision, producing_run_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash) VALUES ($workspaceId, $stableId, $revision, NULL, $runId, $nodeKind, $canonicalKey, $displayName, $filePath, $startLine, $endLine, $provider, $confidence, $evidenceJson, $contentHash);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$stableId", node.StableId),
            ("$revision", revision),
            ("$runId", runId),
            ("$nodeKind", node.NodeKind),
            ("$canonicalKey", node.CanonicalKey),
            ("$displayName", node.DisplayName),
            ("$filePath", node.FilePath),
            ("$startLine", node.StartLine),
            ("$endLine", node.EndLine),
            ("$provider", node.Provider),
            ("$confidence", node.Confidence),
            ("$evidenceJson", node.EvidenceJson),
            ("$contentHash", node.ContentHash));

    private Task CloseNodeVersionAsync(string stableId, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "UPDATE graph_node_versions SET valid_to_revision = $previousRevision WHERE workspace_id = $workspaceId AND stable_id = $stableId AND valid_to_revision IS NULL;",
            cancellationToken,
            ("$previousRevision", revision - 1),
            ("$workspaceId", workspaceId),
            ("$stableId", stableId));

    private async Task CloseMissingNodeVersionsAsync(HashSet<string> incomingNodeIds, CancellationToken cancellationToken)
    {
        foreach (var activeNodeId in await LoadActiveNodeIdsAsync(cancellationToken))
        {
            if (!incomingNodeIds.Contains(activeNodeId))
            {
                await CloseNodeVersionAsync(activeNodeId, cancellationToken);
            }
        }
    }

    private Task PersistEdgeSnapshotAsync(GraphEdgeFact edge, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_edges(revision, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json) VALUES ($revision, $edgeId, $sourceStableId, $targetStableId, $edgeKind, $normalizedJoinKey, $provider, $confidence, $evidenceJson);",
            cancellationToken,
            ("$revision", revision),
            ("$edgeId", edge.EdgeId),
            ("$sourceStableId", edge.SourceStableId),
            ("$targetStableId", edge.TargetStableId),
            ("$edgeKind", edge.EdgeKind),
            ("$normalizedJoinKey", edge.NormalizedJoinKey),
            ("$provider", edge.Provider),
            ("$confidence", edge.Confidence),
            ("$evidenceJson", edge.EvidenceJson));

    private async Task<Problem?> UpsertEdgeIdentityAsync(GraphEdgeFact edge, CancellationToken cancellationToken)
    {
        var identity = await LoadEdgeIdentityAsync(edge.EdgeId, cancellationToken);
        if (identity is not null && !identity.Matches(edge))
        {
            return Problem.Conflict(
                $"Graph edge '{edge.EdgeId}' changed its immutable source, target, kind, or normalized join key.");
        }

        await GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_edge_identities(workspace_id, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, first_seen_revision, last_seen_revision) VALUES ($workspaceId, $edgeId, $sourceStableId, $targetStableId, $edgeKind, $normalizedJoinKey, $revision, $revision) ON CONFLICT(workspace_id, edge_id) DO UPDATE SET last_seen_revision = excluded.last_seen_revision;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$edgeId", edge.EdgeId),
            ("$sourceStableId", edge.SourceStableId),
            ("$targetStableId", edge.TargetStableId),
            ("$edgeKind", edge.EdgeKind),
            ("$normalizedJoinKey", edge.NormalizedJoinKey),
            ("$revision", revision));
        return null;
    }

    private Task InsertEdgeVersionAsync(GraphEdgeFact edge, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO graph_edge_versions(workspace_id, edge_id, valid_from_revision, valid_to_revision, producing_run_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json) VALUES ($workspaceId, $edgeId, $revision, NULL, $runId, $sourceStableId, $targetStableId, $edgeKind, $normalizedJoinKey, $provider, $confidence, $evidenceJson);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$edgeId", edge.EdgeId),
            ("$revision", revision),
            ("$runId", runId),
            ("$sourceStableId", edge.SourceStableId),
            ("$targetStableId", edge.TargetStableId),
            ("$edgeKind", edge.EdgeKind),
            ("$normalizedJoinKey", edge.NormalizedJoinKey),
            ("$provider", edge.Provider),
            ("$confidence", edge.Confidence),
            ("$evidenceJson", edge.EvidenceJson));

    private Task CloseEdgeVersionAsync(string edgeId, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "UPDATE graph_edge_versions SET valid_to_revision = $previousRevision WHERE workspace_id = $workspaceId AND edge_id = $edgeId AND valid_to_revision IS NULL;",
            cancellationToken,
            ("$previousRevision", revision - 1),
            ("$workspaceId", workspaceId),
            ("$edgeId", edgeId));

    private async Task CloseMissingEdgeVersionsAsync(HashSet<string> incomingEdgeIds, CancellationToken cancellationToken)
    {
        foreach (var activeEdgeId in await LoadActiveEdgeIdsAsync(cancellationToken))
        {
            if (!incomingEdgeIds.Contains(activeEdgeId))
            {
                await CloseEdgeVersionAsync(activeEdgeId, cancellationToken);
            }
        }
    }

    private async Task<ActiveNodeVersion?> LoadActiveNodeVersionAsync(string stableId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash FROM graph_node_versions WHERE workspace_id = $workspaceId AND stable_id = $stableId AND valid_to_revision IS NULL;";
        GraphSql.AddParameters(command, [("$workspaceId", workspaceId), ("$stableId", stableId)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActiveNodeVersion(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetString(6),
            reader.GetDouble(7),
            reader.GetString(8),
            reader.GetString(9));
    }

    private async Task<ActiveEdgeVersion?> LoadActiveEdgeVersionAsync(string edgeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json FROM graph_edge_versions WHERE workspace_id = $workspaceId AND edge_id = $edgeId AND valid_to_revision IS NULL;";
        GraphSql.AddParameters(command, [("$workspaceId", workspaceId), ("$edgeId", edgeId)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActiveEdgeVersion(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetDouble(5),
            reader.GetString(6));
    }

    private async Task<EdgeIdentity?> LoadEdgeIdentityAsync(string edgeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT source_stable_id, target_stable_id, edge_kind, normalized_join_key FROM graph_edge_identities WHERE workspace_id = $workspaceId AND edge_id = $edgeId;";
        GraphSql.AddParameters(command, [("$workspaceId", workspaceId), ("$edgeId", edgeId)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EdgeIdentity(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task<string[]> LoadActiveNodeIdsAsync(CancellationToken cancellationToken) =>
        await LoadActiveIdentifiersAsync("graph_node_versions", "stable_id", cancellationToken);

    private async Task<string[]> LoadActiveEdgeIdsAsync(CancellationToken cancellationToken) =>
        await LoadActiveIdentifiersAsync("graph_edge_versions", "edge_id", cancellationToken);

    private async Task<string[]> LoadActiveIdentifiersAsync(string tableName, string identifierColumn, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {identifierColumn} FROM {tableName} WHERE workspace_id = $workspaceId AND valid_to_revision IS NULL;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var identifiers = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(reader.GetString(0));
        }

        return [.. identifiers];
    }

    private sealed record ActiveNodeVersion(
        string NodeKind,
        string CanonicalKey,
        string DisplayName,
        string? FilePath,
        int? StartLine,
        int? EndLine,
        string Provider,
        double Confidence,
        string EvidenceJson,
        string ContentHash)
    {
        public bool Matches(GraphNodeFact node) =>
            string.Equals(NodeKind, node.NodeKind, StringComparison.Ordinal) &&
            string.Equals(CanonicalKey, node.CanonicalKey, StringComparison.Ordinal) &&
            string.Equals(DisplayName, node.DisplayName, StringComparison.Ordinal) &&
            string.Equals(FilePath, node.FilePath, StringComparison.Ordinal) &&
            StartLine == node.StartLine &&
            EndLine == node.EndLine &&
            string.Equals(Provider, node.Provider, StringComparison.Ordinal) &&
            Confidence.Equals(node.Confidence) &&
            string.Equals(EvidenceJson, node.EvidenceJson, StringComparison.Ordinal) &&
            string.Equals(ContentHash, node.ContentHash, StringComparison.Ordinal);
    }

    private sealed record ActiveEdgeVersion(
        string SourceStableId,
        string TargetStableId,
        string EdgeKind,
        string? NormalizedJoinKey,
        string Provider,
        double Confidence,
        string EvidenceJson)
    {
        public bool Matches(GraphEdgeFact edge) =>
            string.Equals(SourceStableId, edge.SourceStableId, StringComparison.Ordinal) &&
            string.Equals(TargetStableId, edge.TargetStableId, StringComparison.Ordinal) &&
            string.Equals(EdgeKind, edge.EdgeKind, StringComparison.Ordinal) &&
            string.Equals(NormalizedJoinKey, edge.NormalizedJoinKey, StringComparison.Ordinal) &&
            string.Equals(Provider, edge.Provider, StringComparison.Ordinal) &&
            Confidence.Equals(edge.Confidence) &&
            string.Equals(EvidenceJson, edge.EvidenceJson, StringComparison.Ordinal);
    }

    private sealed record EdgeIdentity(
        string SourceStableId,
        string TargetStableId,
        string EdgeKind,
        string? NormalizedJoinKey)
    {
        public bool Matches(GraphEdgeFact edge) =>
            string.Equals(SourceStableId, edge.SourceStableId, StringComparison.Ordinal) &&
            string.Equals(TargetStableId, edge.TargetStableId, StringComparison.Ordinal) &&
            string.Equals(EdgeKind, edge.EdgeKind, StringComparison.Ordinal) &&
            string.Equals(NormalizedJoinKey, edge.NormalizedJoinKey, StringComparison.Ordinal);
    }
}
