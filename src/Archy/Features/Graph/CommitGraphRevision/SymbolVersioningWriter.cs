using Microsoft.Data.Sqlite;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

internal sealed class SymbolVersioningWriter(
    SqliteConnection connection,
    SqliteTransaction transaction,
    string workspaceId,
    string runId,
    long revision)
{
    public async Task<Problem?> PersistAsync(
        IReadOnlyList<GraphSymbolFact> symbols,
        IReadOnlyList<InterfaceFingerprintFact> interfaceFingerprints,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in symbols)
        {
            var identityProblem = await UpsertSymbolIdentityAsync(symbol, cancellationToken);
            if (identityProblem is not null)
            {
                return identityProblem;
            }

            var activeVersion = await LoadActiveSymbolVersionAsync(symbol.SymbolId, cancellationToken);
            if (activeVersion is null)
            {
                await InsertSymbolVersionAsync(symbol, cancellationToken);
            }
            else if (!activeVersion.Matches(symbol))
            {
                await CloseSymbolVersionAsync(symbol.SymbolId, cancellationToken);
                await InsertSymbolVersionAsync(symbol, cancellationToken);
            }
        }

        await CloseMissingSymbolVersionsAsync(
            symbols.Select(symbol => symbol.SymbolId).ToHashSet(StringComparer.Ordinal),
            cancellationToken);

        foreach (var interfaceFingerprint in interfaceFingerprints)
        {
            var activeVersion = await LoadActiveFingerprintVersionAsync(
                interfaceFingerprint.SymbolId,
                interfaceFingerprint.FingerprintKind,
                cancellationToken);
            if (activeVersion is null)
            {
                await InsertFingerprintVersionAsync(interfaceFingerprint, cancellationToken);
            }
            else if (!activeVersion.Matches(interfaceFingerprint))
            {
                await CloseFingerprintVersionAsync(
                    interfaceFingerprint.SymbolId,
                    interfaceFingerprint.FingerprintKind,
                    cancellationToken);
                await InsertFingerprintVersionAsync(interfaceFingerprint, cancellationToken);
            }
        }

        await CloseMissingFingerprintVersionsAsync(
            interfaceFingerprints.Select(FingerprintKey.From).ToHashSet(),
            cancellationToken);
        return null;
    }

    private async Task<Problem?> UpsertSymbolIdentityAsync(GraphSymbolFact symbol, CancellationToken cancellationToken)
    {
        var existingNodeStableId = await GraphSql.ScalarStringAsync(
            connection,
            transaction,
            "SELECT node_stable_id FROM symbol_identities WHERE workspace_id = $workspaceId AND symbol_id = $symbolId;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$symbolId", symbol.SymbolId));
        if (existingNodeStableId is not null &&
            !string.Equals(existingNodeStableId, symbol.NodeStableId, StringComparison.Ordinal))
        {
            return Problem.Conflict(
                $"Symbol '{symbol.SymbolId}' changed the graph node that owns its durable identity.");
        }

        await GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO symbol_identities(workspace_id, symbol_id, node_stable_id, first_seen_revision, last_seen_revision) VALUES ($workspaceId, $symbolId, $nodeStableId, $revision, $revision) ON CONFLICT(workspace_id, symbol_id) DO UPDATE SET last_seen_revision = excluded.last_seen_revision;",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$symbolId", symbol.SymbolId),
            ("$nodeStableId", symbol.NodeStableId),
            ("$revision", revision));
        return null;
    }

    private Task InsertSymbolVersionAsync(GraphSymbolFact symbol, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO symbol_versions(workspace_id, symbol_id, valid_from_revision, valid_to_revision, producing_run_id, node_stable_id, fully_qualified_name, visibility, normalized_signature, parameter_metadata_json, return_metadata_json, signature_hash) VALUES ($workspaceId, $symbolId, $revision, NULL, $runId, $nodeStableId, $fullyQualifiedName, $visibility, $normalizedSignature, $parameterMetadataJson, $returnMetadataJson, $signatureHash);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$symbolId", symbol.SymbolId),
            ("$revision", revision),
            ("$runId", runId),
            ("$nodeStableId", symbol.NodeStableId),
            ("$fullyQualifiedName", symbol.FullyQualifiedName),
            ("$visibility", symbol.Visibility),
            ("$normalizedSignature", symbol.NormalizedSignature),
            ("$parameterMetadataJson", symbol.ParameterMetadataJson),
            ("$returnMetadataJson", symbol.ReturnMetadataJson),
            ("$signatureHash", symbol.SignatureHash));

    private Task CloseSymbolVersionAsync(string symbolId, CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "UPDATE symbol_versions SET valid_to_revision = $previousRevision WHERE workspace_id = $workspaceId AND symbol_id = $symbolId AND valid_to_revision IS NULL;",
            cancellationToken,
            ("$previousRevision", revision - 1),
            ("$workspaceId", workspaceId),
            ("$symbolId", symbolId));

    private async Task CloseMissingSymbolVersionsAsync(HashSet<string> incomingSymbolIds, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT symbol_id FROM symbol_versions WHERE workspace_id = $workspaceId AND valid_to_revision IS NULL;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activeSymbolIds = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            activeSymbolIds.Add(reader.GetString(0));
        }

        foreach (var activeSymbolId in activeSymbolIds)
        {
            if (!incomingSymbolIds.Contains(activeSymbolId))
            {
                await CloseSymbolVersionAsync(activeSymbolId, cancellationToken);
            }
        }
    }

    private Task InsertFingerprintVersionAsync(
        InterfaceFingerprintFact interfaceFingerprint,
        CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO interface_fingerprint_versions(workspace_id, symbol_id, fingerprint_kind, valid_from_revision, valid_to_revision, producing_run_id, fingerprint_hash, normalized_members_json) VALUES ($workspaceId, $symbolId, $fingerprintKind, $revision, NULL, $runId, $fingerprintHash, $normalizedMembersJson);",
            cancellationToken,
            ("$workspaceId", workspaceId),
            ("$symbolId", interfaceFingerprint.SymbolId),
            ("$fingerprintKind", interfaceFingerprint.FingerprintKind),
            ("$revision", revision),
            ("$runId", runId),
            ("$fingerprintHash", interfaceFingerprint.FingerprintHash),
            ("$normalizedMembersJson", interfaceFingerprint.NormalizedMembersJson));

    private Task CloseFingerprintVersionAsync(
        string symbolId,
        string fingerprintKind,
        CancellationToken cancellationToken) =>
        GraphSql.ExecuteAsync(
            connection,
            transaction,
            "UPDATE interface_fingerprint_versions SET valid_to_revision = $previousRevision WHERE workspace_id = $workspaceId AND symbol_id = $symbolId AND fingerprint_kind = $fingerprintKind AND valid_to_revision IS NULL;",
            cancellationToken,
            ("$previousRevision", revision - 1),
            ("$workspaceId", workspaceId),
            ("$symbolId", symbolId),
            ("$fingerprintKind", fingerprintKind));

    private async Task CloseMissingFingerprintVersionsAsync(
        HashSet<FingerprintKey> incomingFingerprints,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT symbol_id, fingerprint_kind FROM interface_fingerprint_versions WHERE workspace_id = $workspaceId AND valid_to_revision IS NULL;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activeFingerprints = new List<FingerprintKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            activeFingerprints.Add(new FingerprintKey(reader.GetString(0), reader.GetString(1)));
        }

        foreach (var activeFingerprint in activeFingerprints)
        {
            if (!incomingFingerprints.Contains(activeFingerprint))
            {
                await CloseFingerprintVersionAsync(
                    activeFingerprint.SymbolId,
                    activeFingerprint.FingerprintKind,
                    cancellationToken);
            }
        }
    }

    private async Task<ActiveSymbolVersion?> LoadActiveSymbolVersionAsync(string symbolId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT node_stable_id, fully_qualified_name, visibility, normalized_signature, parameter_metadata_json, return_metadata_json, signature_hash FROM symbol_versions WHERE workspace_id = $workspaceId AND symbol_id = $symbolId AND valid_to_revision IS NULL;";
        GraphSql.AddParameters(command, [("$workspaceId", workspaceId), ("$symbolId", symbolId)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActiveSymbolVersion(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private async Task<ActiveFingerprintVersion?> LoadActiveFingerprintVersionAsync(
        string symbolId,
        string fingerprintKind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT fingerprint_hash, normalized_members_json FROM interface_fingerprint_versions WHERE workspace_id = $workspaceId AND symbol_id = $symbolId AND fingerprint_kind = $fingerprintKind AND valid_to_revision IS NULL;";
        GraphSql.AddParameters(command,
        [
            ("$workspaceId", workspaceId),
            ("$symbolId", symbolId),
            ("$fingerprintKind", fingerprintKind),
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActiveFingerprintVersion(reader.GetString(0), reader.GetString(1));
    }

    private sealed record ActiveSymbolVersion(
        string NodeStableId,
        string FullyQualifiedName,
        string Visibility,
        string NormalizedSignature,
        string ParameterMetadataJson,
        string ReturnMetadataJson,
        string SignatureHash)
    {
        public bool Matches(GraphSymbolFact symbol) =>
            string.Equals(NodeStableId, symbol.NodeStableId, StringComparison.Ordinal) &&
            string.Equals(FullyQualifiedName, symbol.FullyQualifiedName, StringComparison.Ordinal) &&
            string.Equals(Visibility, symbol.Visibility, StringComparison.Ordinal) &&
            string.Equals(NormalizedSignature, symbol.NormalizedSignature, StringComparison.Ordinal) &&
            string.Equals(ParameterMetadataJson, symbol.ParameterMetadataJson, StringComparison.Ordinal) &&
            string.Equals(ReturnMetadataJson, symbol.ReturnMetadataJson, StringComparison.Ordinal) &&
            string.Equals(SignatureHash, symbol.SignatureHash, StringComparison.Ordinal);
    }

    private sealed record ActiveFingerprintVersion(string FingerprintHash, string NormalizedMembersJson)
    {
        public bool Matches(InterfaceFingerprintFact interfaceFingerprint) =>
            string.Equals(FingerprintHash, interfaceFingerprint.FingerprintHash, StringComparison.Ordinal) &&
            string.Equals(NormalizedMembersJson, interfaceFingerprint.NormalizedMembersJson, StringComparison.Ordinal);
    }

    private readonly record struct FingerprintKey(string SymbolId, string FingerprintKind)
    {
        public static FingerprintKey From(InterfaceFingerprintFact interfaceFingerprint) => new(
            interfaceFingerprint.SymbolId,
            interfaceFingerprint.FingerprintKind);
    }
}
