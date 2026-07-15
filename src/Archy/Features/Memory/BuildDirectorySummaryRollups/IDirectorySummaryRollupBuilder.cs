namespace Archy.Features.Memory.BuildDirectorySummaryRollups;

public interface IDirectorySummaryRollupBuilder
{
    DirectorySummaryRollup Build(
        string repositoryRelativeDirectory,
        IReadOnlyList<DirectorySummaryChild> children);
}
