using Archy.SharedKernel.Primitives;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

/// <summary>Validates the deterministic, repository-scoped layer-rule configuration.</summary>
internal static class LayerRuleConfigurationValidator
{
    public static Problem? Validate(IReadOnlyList<LayerRuleConfiguration>? layers)
    {
        if (layers is null)
        {
            return Problem.Validation("layers must be an array when architecture rules are configured.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            if (layer is null ||
                !IsLayerName(layer.Name) ||
                layer.Includes is null ||
                layer.Includes.Length == 0 ||
                layer.Includes.Any(static pattern => !IsRepositoryRelativePattern(pattern)) ||
                layer.Includes.Distinct(StringComparer.Ordinal).Count() != layer.Includes.Length ||
                layer.MayDependOn is null ||
                layer.MayDependOn.Any(static name => !IsLayerName(name)) ||
                layer.MayDependOn.Distinct(StringComparer.Ordinal).Count() != layer.MayDependOn.Length ||
                layer.MayDependOn.Contains(layer.Name, StringComparer.Ordinal) ||
                !names.Add(layer.Name))
            {
                return Problem.Validation("Each layer requires a unique name, unique repository-relative include patterns, and unique non-self dependency targets.");
            }
        }

        if (layers.Any(layer => layer.MayDependOn.Any(target => !names.Contains(target))))
        {
            return Problem.Validation("Every layer may_depend_on target must name another declared layer.");
        }

        return null;
    }

    public static bool IsLayerName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        value.All(static character => !char.IsControl(character));

    public static bool IsRepositoryRelativePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) ||
            !string.Equals(pattern, pattern.Trim(), StringComparison.Ordinal) ||
            pattern.Contains('\0'))
        {
            return false;
        }

        var normalized = pattern.Replace('\\', '/');
        if (normalized.StartsWith('/') ||
            normalized.StartsWith("./", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        try
        {
            _ = new RepositoryPathGlob(normalized, matchAnySegment: false);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
