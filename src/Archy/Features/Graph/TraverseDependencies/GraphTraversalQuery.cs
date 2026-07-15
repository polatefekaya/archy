namespace Archy.Features.Graph.TraverseDependencies;

public sealed record GraphTraversalQuery(
    string StartStableId,
    GraphTraversalDirection Direction,
    long? Revision = null,
    int MaxDepth = 3,
    int MaxEdges = 100);
