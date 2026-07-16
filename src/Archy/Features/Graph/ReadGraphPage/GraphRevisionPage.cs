using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Graph.ReadGraphPage;

/// <summary>A bounded, homogeneous page of immutable graph facts.</summary>
public sealed record GraphRevisionPage(
    GraphRevisionFactKind FactKind,
    long Revision,
    int Offset,
    int Limit,
    int TotalCount,
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges);
