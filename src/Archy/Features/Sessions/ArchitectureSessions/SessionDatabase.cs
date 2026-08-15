using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ArchitectureSessions;

internal static class SessionDatabase
{
    internal static async Task ExecuteAsync(
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

    internal static async Task<long> ScalarLongAsync(
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
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    internal static async Task<string?> ScalarStringAsync(
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
        return value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    internal static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters) =>
        await ScalarLongAsync(connection, transaction, sql, cancellationToken, parameters) == 1;

    internal static void ValidateJson(string value)
    {
        using var _ = JsonDocument.Parse(value);
    }

    internal static string ToDatabase(SessionEventKind kind) => kind switch
    {
        SessionEventKind.SessionStarted => "session_started",
        SessionEventKind.FileTouched => "file_touched",
        SessionEventKind.ValidationCompleted => "validation_completed",
        SessionEventKind.PreflightContextRecorded => "preflight_context_recorded",
        SessionEventKind.PreflightContextInvalidated => "preflight_context_invalidated",
        SessionEventKind.DecisionRecorded => "decision_recorded",
        SessionEventKind.SummaryBatchRequested => "summary_batch_requested",
        SessionEventKind.SessionEnded => "session_ended",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown session event kind."),
    };

    internal static SessionEventKind EventKindFromDatabase(string value) => value switch
    {
        "session_started" => SessionEventKind.SessionStarted,
        "file_touched" => SessionEventKind.FileTouched,
        "validation_completed" => SessionEventKind.ValidationCompleted,
        "preflight_context_recorded" => SessionEventKind.PreflightContextRecorded,
        "preflight_context_invalidated" => SessionEventKind.PreflightContextInvalidated,
        "decision_recorded" => SessionEventKind.DecisionRecorded,
        "summary_batch_requested" => SessionEventKind.SummaryBatchRequested,
        "session_ended" => SessionEventKind.SessionEnded,
        _ => throw new InvalidOperationException($"Unknown persisted session event kind '{value}'."),
    };

    internal static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }
    }
}
