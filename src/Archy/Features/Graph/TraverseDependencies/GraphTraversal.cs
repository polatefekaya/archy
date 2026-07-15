namespace Archy.Features.Graph.TraverseDependencies;

public sealed record GraphTraversal(
    long Revision,
    string StartStableId,
    GraphTraversalDirection Direction,
    IReadOnlyList<TraversedGraphEdge> Edges,
    bool IsTruncated);
