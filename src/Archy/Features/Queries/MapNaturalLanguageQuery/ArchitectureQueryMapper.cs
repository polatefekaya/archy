using System.Text.RegularExpressions;
using Archy.Features.Graph.TraverseDependencies;

namespace Archy.Features.Queries.MapNaturalLanguageQuery;

/// <summary>Maps a deliberately small, documented query grammar without heuristic or model inference.</summary>
public static partial class ArchitectureQueryMapper
{
    public static ArchitectureQueryMapping Map(string input, long? revision = null)
    {
        if (string.IsNullOrWhiteSpace(input) || revision is < 1) return Unsupported();
        var normalized = input.Trim();
        var match = DeleteOrUses().Match(normalized);
        if (match.Success) return Result(match.Groups[1].Value, GraphTraversalDirection.Dependents, revision);
        match = Uses().Match(normalized);
        return match.Success ? Result(match.Groups[1].Value, GraphTraversalDirection.Dependencies, revision) : Unsupported();
    }

    private static ArchitectureQueryMapping Result(string stableId, GraphTraversalDirection direction, long? revision) =>
        string.IsNullOrWhiteSpace(stableId) || stableId.Length > 1024 ? Unsupported() : new(new ArchitectureQueryIntent(stableId.Trim(), direction, 3, revision), null);
    private static ArchitectureQueryMapping Unsupported() => new(null, "Supported queries: 'what breaks if I delete <stable-id>?', 'what uses <stable-id>?', and 'what does <stable-id> use?'.");
    [GeneratedRegex("^(?:what breaks if i delete|what uses)\\s+(.+?)\\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DeleteOrUses();
    [GeneratedRegex("^what does\\s+(.+?)\\s+use\\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Uses();
}
