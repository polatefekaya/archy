namespace Archy.Features.Analysis.InventorySources;

public sealed record SourcePathExclusion(
    string RepositoryRelativePath,
    SourcePathExclusionReason Reason,
    string MatchedRule);
