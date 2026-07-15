using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Storage.WorkspaceDatabase.Check;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;

public sealed partial class WorkspaceDatabaseDiagnosticsExporter(
    TimeProvider timeProvider,
    WorkspaceDatabaseChecker checker)
{
    public async ValueTask<Result<DatabaseDiagnosticsExport>> ExportAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var check = await checker.CheckAsync(location, cancellationToken);
        if (!check.IsSuccess)
        {
            return ResultFactory.Failure<DatabaseDiagnosticsExport>(check.Problem!);
        }

        var createdAt = timeProvider.GetUtcNow();
        var temporaryPath = string.Empty;
        try
        {
            Directory.CreateDirectory(location.DatabaseDiagnosticsDirectory);
            var exportPath = Path.Combine(
                location.DatabaseDiagnosticsDirectory,
                $"archy-db-diagnostics-{createdAt:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
            temporaryPath = $"{exportPath}.tmp";
            var payload = new DatabaseDiagnosticsPayload(
                1,
                createdAt,
                location.WorkspaceId,
                check.Value,
                new FileInfo(location.DatabasePath).Length,
                Directory.Exists(location.DatabaseBackupDirectory)
                    ? Directory.GetFiles(location.DatabaseBackupDirectory, "*.db").Length
                    : 0);
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    payload,
                    DatabaseDiagnosticsJsonContext.Default.DatabaseDiagnosticsPayload,
                    cancellationToken);
            }

            File.Move(temporaryPath, exportPath, overwrite: false);
            return ResultFactory.Success(new DatabaseDiagnosticsExport(
                exportPath,
                createdAt,
                check.Value,
                payload.DatabaseByteCount,
                payload.BackupCount));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<DatabaseDiagnosticsExport>(
                Problem.Storage($"Archy could not export database diagnostics: {exception.Message}"));
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath) && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record DatabaseDiagnosticsPayload(
        int FormatVersion,
        DateTimeOffset CreatedAtUtc,
        string WorkspaceId,
        DatabaseIntegrityReport Integrity,
        long DatabaseByteCount,
        int BackupCount);

    [JsonSerializable(typeof(DatabaseDiagnosticsPayload))]
    private sealed partial class DatabaseDiagnosticsJsonContext : JsonSerializerContext;
}
