using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Graph.ReadGraphRevision;

/// <summary>One immutable graph revision materialized for deterministic rule evaluation.</summary>
public sealed record GraphRevisionSnapshot(
    long Revision,
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<GraphSymbolFact> Symbols,
    IReadOnlyList<InterfaceFingerprintFact> InterfaceFingerprints);
