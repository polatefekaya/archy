namespace Archy.Features.Storage.AnalysisRuns;

public sealed record AnalysisRun(
    string RunId,
    string WorkspaceId,
    string AnalyzerVersion,
    string ConfigurationHash,
    string? RepositoryCommit,
    AnalysisRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long? GraphRevision);
