namespace Archy.Features.Analysis.MineGitCoChanges;

/// <summary>Non-semantic temporal coupling; callers must persist it separately from semantic dependency evidence.</summary>
public sealed record CoChangeEdge(string LeftRepositoryRelativePath, string RightRepositoryRelativePath, int CoChangeCount, double Score);
