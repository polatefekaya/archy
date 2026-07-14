namespace Archy.Features.Graph.CommitGraphRevision;

public sealed record GraphEdgeFact(
    string EdgeId,
    string SourceStableId,
    string TargetStableId,
    string EdgeKind,
    string? NormalizedJoinKey,
    string Provider,
    double Confidence,
    string EvidenceJson);
