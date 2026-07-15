using Archy.SharedKernel.Primitives;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

/// <summary>Prevents advisory or ambiguous edge categories from entering hard-rule configuration.</summary>
internal static class ArchitectureEnforcementConfigurationValidator
{
    public static Problem? Validate(ArchitectureEnforcementConfiguration? configuration)
    {
        if (configuration is null ||
            configuration.HardEdgeKinds is null ||
            configuration.HardEdgeKinds.Length == 0 ||
            configuration.HardEdgeKinds.Any(static edgeKind => !IsEdgeKind(edgeKind)) ||
            configuration.HardEdgeKinds.Distinct(StringComparer.Ordinal).Count() != configuration.HardEdgeKinds.Length)
        {
            return Problem.Validation("enforcement.hard_edge_kinds must contain unique, lowercase deterministic edge-kind identifiers.");
        }

        return null;
    }

    public static bool IsEdgeKind(string? edgeKind) =>
        !string.IsNullOrWhiteSpace(edgeKind) &&
        edgeKind.Length <= 128 &&
        string.Equals(edgeKind, edgeKind.Trim(), StringComparison.Ordinal) &&
        edgeKind.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
