using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.LayerMembership;

/// <summary>
/// Resolves code-node membership from validated repository-relative layer globs.
/// Virtual nodes without a source file are intentionally outside layer enforcement.
/// </summary>
public sealed class LayerMembershipResolver : ILayerMembershipResolver
{
    public Result<IReadOnlyList<LayerMembershipResolution>> Resolve(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<LayerRuleConfiguration> layers)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(layers);
        var rulesProblem = LayerRuleConfigurationValidator.Validate(layers);
        if (rulesProblem is not null)
        {
            return ResultFactory.Failure<IReadOnlyList<LayerMembershipResolution>>(rulesProblem);
        }

        if (nodes.Any(static node => node is null || string.IsNullOrWhiteSpace(node.StableId)) ||
            nodes.Select(static node => node.StableId).Distinct(StringComparer.Ordinal).Count() != nodes.Count ||
            nodes.Any(node => node.FilePath is not null && !IsRepositoryRelativePath(node.FilePath)))
        {
            return ResultFactory.Failure<IReadOnlyList<LayerMembershipResolution>>(
                Problem.Validation("Layer membership requires unique graph nodes with repository-relative file paths when a source path is present."));
        }

        var compiledRules = layers
            .Select(static layer => new CompiledLayerRule(
                layer.Name,
                layer.Includes.Select(pattern => new RepositoryPathGlob(pattern, HasNoDirectorySeparator(pattern))).ToArray()))
            .ToArray();
        var results = nodes
            .OrderBy(static node => node.StableId, StringComparer.Ordinal)
            .Select(node => ResolveNode(node, compiledRules))
            .ToArray();
        return ResultFactory.Success<IReadOnlyList<LayerMembershipResolution>>(results);
    }

    private static LayerMembershipResolution ResolveNode(GraphNodeFact node, IReadOnlyList<CompiledLayerRule> rules)
    {
        if (node.FilePath is null)
        {
            return new LayerMembershipResolution(
                node.StableId,
                LayerMembershipState.NotApplicable,
                null,
                [],
                "The graph node has no repository source path and is outside layer enforcement.");
        }

        var matches = rules
            .Where(rule => rule.Patterns.Any(pattern => pattern.IsMatch(node.FilePath)))
            .Select(static rule => rule.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        return matches.Length switch
        {
            0 => new LayerMembershipResolution(node.StableId, LayerMembershipState.Unassigned, null, [], "No configured layer include pattern matched the source path."),
            1 => new LayerMembershipResolution(node.StableId, LayerMembershipState.Assigned, matches[0], matches, null),
            _ => new LayerMembershipResolution(node.StableId, LayerMembershipState.Ambiguous, null, matches, "More than one configured layer include pattern matched the source path."),
        };
    }

    private static bool HasNoDirectorySeparator(string pattern) =>
        !pattern.TrimStart('/').Contains('/', StringComparison.Ordinal);

    private static bool IsRepositoryRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        return !normalized.StartsWith('/') &&
               !(normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':') &&
               normalized.Split('/', StringSplitOptions.None).All(static segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private sealed record CompiledLayerRule(string Name, IReadOnlyList<RepositoryPathGlob> Patterns);
}
