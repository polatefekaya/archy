namespace Archy.Features.Analysis.MineGitCoChanges;

public sealed record CoChangeMiningRequest(string RepositoryRoot, int MaximumCommits = 500, int MinimumCoChanges = 2);
