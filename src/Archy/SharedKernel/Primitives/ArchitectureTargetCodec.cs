namespace Archy.SharedKernel.Primitives;

public static class ArchitectureTargetCodec
{
    public static string ToStorageValue(ArchitectureTargetKind kind) => kind switch
    {
        ArchitectureTargetKind.GraphNode => "graph_node",
        ArchitectureTargetKind.GraphEdge => "graph_edge",
        ArchitectureTargetKind.GraphSymbol => "graph_symbol",
        ArchitectureTargetKind.Rule => "rule",
        ArchitectureTargetKind.DuplicateFinding => "duplicate_finding",
        ArchitectureTargetKind.PlacementFinding => "placement_finding",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown architecture target kind."),
    };

    public static ArchitectureTargetKind FromStorageValue(string value) => value switch
    {
        "graph_node" => ArchitectureTargetKind.GraphNode,
        "graph_edge" => ArchitectureTargetKind.GraphEdge,
        "graph_symbol" => ArchitectureTargetKind.GraphSymbol,
        "rule" => ArchitectureTargetKind.Rule,
        "duplicate_finding" => ArchitectureTargetKind.DuplicateFinding,
        "placement_finding" => ArchitectureTargetKind.PlacementFinding,
        _ => throw new InvalidOperationException($"Unknown persisted architecture target kind '{value}'."),
    };
}
