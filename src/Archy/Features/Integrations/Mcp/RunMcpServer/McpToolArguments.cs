using System.Text.Json;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal static class McpToolArguments
{
    public static bool TryGetRequiredString(JsonElement arguments, string propertyName, out string value)
    {
        value = string.Empty;
        return arguments.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    public static string GetPayload(JsonElement arguments, string propertyName) =>
        arguments.TryGetProperty(propertyName, out var payload) ? payload.GetRawText() : "{}";

    public static bool TryGetResolution(JsonElement arguments, out DecisionResolution resolution)
    {
        resolution = default;
        return arguments.TryGetProperty("resolution", out var rawResolution)
            && rawResolution.ValueKind == JsonValueKind.String
            && Enum.TryParse(rawResolution.GetString(), ignoreCase: true, out resolution)
            && Enum.IsDefined(resolution);
    }

    public static bool TryGetTarget(JsonElement arguments, out ArchitectureTarget target) =>
        TryGetTarget(arguments, "target", out target);

    public static bool TryGetTarget(JsonElement arguments, string propertyName, out ArchitectureTarget target)
    {
        target = null!;
        if (!arguments.TryGetProperty(propertyName, out var rawTarget))
        {
            return false;
        }

        return TryParseTarget(rawTarget, out target);
    }

    private static bool TryParseTarget(JsonElement rawTarget, out ArchitectureTarget target)
    {
        target = null!;
        if (rawTarget.ValueKind != JsonValueKind.Object
            || !rawTarget.TryGetProperty("kind", out var rawKind)
            || rawKind.ValueKind != JsonValueKind.String
            || !TryParseTargetKind(rawKind.GetString()!, out var kind)
            || !rawTarget.TryGetProperty("stableId", out var rawStableId)
            || rawStableId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(rawStableId.GetString()))
        {
            return false;
        }

        target = new ArchitectureTarget(kind, rawStableId.GetString()!);
        return true;
    }

    private static bool TryParseTargetKind(string value, out ArchitectureTargetKind kind)
    {
        if (Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind))
        {
            return true;
        }

        kind = value switch
        {
            "graph_node" => ArchitectureTargetKind.GraphNode,
            "graph_edge" => ArchitectureTargetKind.GraphEdge,
            "graph_symbol" => ArchitectureTargetKind.GraphSymbol,
            "rule" => ArchitectureTargetKind.Rule,
            "duplicate_finding" => ArchitectureTargetKind.DuplicateFinding,
            "placement_finding" => ArchitectureTargetKind.PlacementFinding,
            _ => default,
        };
        return value is "graph_node" or "graph_edge" or "graph_symbol" or "rule" or "duplicate_finding" or "placement_finding";
    }

    public static bool TryGetTargets(JsonElement arguments, out IReadOnlyList<ArchitectureTarget> targets)
    {
        targets = [];
        if (!arguments.TryGetProperty("targets", out var rawTargets)
            || rawTargets.ValueKind != JsonValueKind.Array
            || rawTargets.GetArrayLength() == 0)
        {
            return false;
        }

        var parsed = new List<ArchitectureTarget>();
        foreach (var rawTarget in rawTargets.EnumerateArray())
        {
            if (!TryParseTarget(rawTarget, out var target)
                || parsed.Any(existing => existing.Kind == target.Kind && existing.StableId == target.StableId))
            {
                return false;
            }

            parsed.Add(target);
        }

        targets = parsed;
        return true;
    }
}
