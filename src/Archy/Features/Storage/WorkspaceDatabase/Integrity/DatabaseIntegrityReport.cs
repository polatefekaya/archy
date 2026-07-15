namespace Archy.Features.Storage.WorkspaceDatabase.Integrity;

public sealed record DatabaseIntegrityReport(
    bool IsHealthy,
    int SchemaVersion,
    long? ActiveGraphRevision,
    string IntegrityCheckResult,
    IReadOnlyList<string> ForeignKeyViolations);
