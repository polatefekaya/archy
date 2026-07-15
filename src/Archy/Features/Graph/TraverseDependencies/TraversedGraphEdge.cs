namespace Archy.Features.Graph.TraverseDependencies;

public sealed record TraversedGraphEdge(
    int Depth,
    string EdgeId,
    string SourceStableId,
    string TargetStableId,
    string EdgeKind,
    string? NormalizedJoinKey,
    string Provider,
    double Confidence,
    string EvidenceJson);
