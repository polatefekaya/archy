namespace Archy.Features.Graph.ReadGraphPage;

/// <summary>A bounded page request over one immutable graph revision.</summary>
public sealed record GraphRevisionPageQuery(
    GraphRevisionFactKind FactKind,
    long? Revision,
    int Offset,
    int Limit);
