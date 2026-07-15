namespace Archy.Features.Storage.WorkspaceDatabase.Restore;

public sealed record DatabaseRestore(
    string BackupPath,
    int SchemaVersion,
    DateTimeOffset RestoredAtUtc);
