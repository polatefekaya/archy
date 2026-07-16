namespace Archy.Features.Graph.RenderGraphMap;

/// <summary>Lean browser projection of a graph node. Evidence remains available through the inspector API and is never duplicated into the map payload.</summary>
public sealed record GraphMapNode(
    string StableId,
    string NodeKind,
    string CanonicalKey,
    string DisplayName,
    string? FilePath,
    string Provider,
    double Confidence);

/// <summary>Lean browser projection of an architecture dependency.</summary>
public sealed record GraphMapEdge(
    string EdgeId,
    string SourceStableId,
    string TargetStableId,
    string EdgeKind,
    double Confidence);

/// <summary>A bounded but complete-when-possible graph map suitable for canvas rendering.</summary>
public sealed record GraphMapSnapshot(
    long Revision,
    int TotalNodeCount,
    int TotalEdgeCount,
    bool IsTruncated,
    IReadOnlyList<GraphMapNode> Nodes,
    IReadOnlyList<GraphMapEdge> Edges);
