using Archy.Features.Memory.Summaries;

namespace Archy.Features.Memory.BuildDirectorySummaryRollups;

/// <summary>One current child summary selected by the caller; raw source is deliberately absent from this contract.</summary>
public sealed record DirectorySummaryChild(
    string RepositoryRelativePath,
    string TargetStableId,
    SummaryVersion SummaryVersion);
