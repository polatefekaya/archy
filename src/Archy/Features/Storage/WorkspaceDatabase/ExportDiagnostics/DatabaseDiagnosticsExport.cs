using Archy.Features.Storage.WorkspaceDatabase.Integrity;

namespace Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;

public sealed record DatabaseDiagnosticsExport(
    string ExportPath,
    DateTimeOffset CreatedAtUtc,
    DatabaseIntegrityReport Integrity,
    long DatabaseByteCount,
    int BackupCount);
