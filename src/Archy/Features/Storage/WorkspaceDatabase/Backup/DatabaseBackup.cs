namespace Archy.Features.Storage.WorkspaceDatabase.Backup;

public sealed record DatabaseBackup(
    string BackupPath,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc);
