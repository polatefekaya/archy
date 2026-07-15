using Archy.Features.Workspaces.ReadRepositoryCommit;

namespace Archy.Features.Analysis.AnalysisRuns;

public sealed record AnalysisRun(
    string RunId,
    string WorkspaceId,
    string AnalyzerVersion,
    string ConfigurationHash,
    string? RepositoryCommit,
    AnalysisRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long? GraphRevision,
    RepositoryProvenance? RepositoryProvenance = null);
