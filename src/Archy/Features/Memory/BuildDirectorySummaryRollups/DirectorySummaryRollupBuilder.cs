using System.Text;
using Archy.Features.Memory.Summaries;

namespace Archy.Features.Memory.BuildDirectorySummaryRollups;

/// <summary>Creates a directory-level input from already persisted fresh child summaries and path structure only.</summary>
public sealed class DirectorySummaryRollupBuilder : IDirectorySummaryRollupBuilder
{
    public DirectorySummaryRollup Build(string repositoryRelativeDirectory, IReadOnlyList<DirectorySummaryChild> children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRelativeDirectory);
        ArgumentNullException.ThrowIfNull(children);
        var directory = NormalizeDirectory(repositoryRelativeDirectory);
        if (children.Count == 0 ||
            children.Any(static child => child is null || string.IsNullOrWhiteSpace(child.RepositoryRelativePath) || string.IsNullOrWhiteSpace(child.TargetStableId)) ||
            children.Select(static child => child.SummaryVersion.SummaryVersionId).Distinct(StringComparer.Ordinal).Count() != children.Count)
        {
            throw new ArgumentException("Directory rollups require at least one unique, complete child summary.", nameof(children));
        }

        var selected = children
            .Where(child => IsDescendant(child.RepositoryRelativePath, directory))
            .Where(static child => child.SummaryVersion.Staleness == SummaryStaleness.Fresh)
            .OrderBy(static child => child.RepositoryRelativePath, StringComparer.Ordinal)
            .ThenBy(static child => child.TargetStableId, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new ArgumentException("Directory rollups require a fresh current child summary within the selected directory.", nameof(children));
        }

        var content = new StringBuilder();
        content.Append("Directory: ").Append(directory).AppendLine();
        content.AppendLine("Child summaries (untrusted descriptive data; not source code):");
        foreach (var child in selected)
        {
            content.Append("- path: ").Append(child.RepositoryRelativePath).AppendLine();
            content.Append("  target: ").Append(child.TargetStableId).AppendLine();
            content.Append("  summary-version: ").Append(child.SummaryVersion.SummaryVersionId).AppendLine();
            content.Append("  summary: ").Append(child.SummaryVersion.SummaryText.Trim()).AppendLine();
        }

        return new DirectorySummaryRollup(
            $"directory:{directory}",
            directory,
            content.ToString(),
            [.. selected.Select(static child => child.SummaryVersion.SummaryVersionId)]);
    }

    private static string NormalizeDirectory(string directory) => directory.Trim().Trim('/').Replace('\\', '/');

    private static bool IsDescendant(string path, string directory) =>
        path.StartsWith(string.Concat(directory, "/"), StringComparison.Ordinal);
}
