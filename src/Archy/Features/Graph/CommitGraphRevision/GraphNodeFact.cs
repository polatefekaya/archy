namespace Archy.Features.Graph.CommitGraphRevision;

public sealed record GraphNodeFact(
    string StableId,
    string NodeKind,
    string CanonicalKey,
    string DisplayName,
    string? FilePath,
    int? StartLine,
    int? EndLine,
    string Provider,
    double Confidence,
    string EvidenceJson,
    string ContentHash);
