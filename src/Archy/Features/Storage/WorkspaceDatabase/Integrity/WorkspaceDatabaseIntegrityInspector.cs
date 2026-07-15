using Microsoft.Data.Sqlite;

namespace Archy.Features.Storage.WorkspaceDatabase.Integrity;

internal static class WorkspaceDatabaseIntegrityInspector
{
    internal static async Task<DatabaseIntegrityReport> InspectAsync(
        SqliteConnection connection,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var integrityCheck = await WorkspaceDatabaseSql.ReadPragmaStringAsync(connection, "integrity_check", cancellationToken);
        var foreignKeyViolations = await ReadForeignKeyViolationsAsync(connection, cancellationToken);
        var schemaVersion = await WorkspaceDatabaseSql.ReadSchemaVersionAsync(connection, cancellationToken);
        var activeGraphRevision = await WorkspaceDatabaseSql.ReadActiveGraphRevisionAsync(connection, workspaceId, cancellationToken);
        var hasCrossWorkspaceActiveRevision = await HasCrossWorkspaceActiveRevisionAsync(connection, workspaceId, cancellationToken);
        return new DatabaseIntegrityReport(
            string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase) &&
            foreignKeyViolations.Count == 0 &&
            !hasCrossWorkspaceActiveRevision,
            schemaVersion,
            activeGraphRevision,
            integrityCheck,
            foreignKeyViolations);
    }

    private static async Task<IReadOnlyList<string>> ReadForeignKeyViolationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var violations = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            violations.Add($"{reader.GetString(0)}->{reader.GetString(2)}");
        }

        return [.. violations];
    }

    private static async Task<bool> HasCrossWorkspaceActiveRevisionAsync(
        SqliteConnection connection,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM workspace_graph_states state LEFT JOIN graph_revisions revision ON revision.revision = state.active_graph_revision WHERE state.workspace_id = $workspaceId AND state.active_graph_revision IS NOT NULL AND (revision.revision IS NULL OR revision.workspace_id <> state.workspace_id));";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
