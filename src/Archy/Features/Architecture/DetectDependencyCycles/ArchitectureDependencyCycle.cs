namespace Archy.Features.Architecture.DetectDependencyCycles;

/// <summary>Shortest deterministic explanatory path for one strongly connected component.</summary>
public sealed record ArchitectureDependencyCycle(
    IReadOnlyList<string> NodePath,
    IReadOnlyList<string> EdgeIds)
{
    /// <summary>All members of the strongly connected component, independent of the chosen shortest evidence path.</summary>
    public IReadOnlyList<string> ComponentNodeStableIds { get; init; } =
        [.. NodePath.Distinct(StringComparer.Ordinal).OrderBy(static node => node, StringComparer.Ordinal)];
}
