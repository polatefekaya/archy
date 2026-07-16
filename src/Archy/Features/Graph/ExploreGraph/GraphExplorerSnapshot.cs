using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Graph.ExploreGraph;

/// <summary>The data required to render one navigable graph neighbourhood without loading a whole repository graph into the browser.</summary>
public sealed record GraphExplorerSnapshot(
    long Revision,
    int TotalNodeCount,
    int TotalEdgeCount,
    string CenterStableId,
    bool IsFocused,
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges);
