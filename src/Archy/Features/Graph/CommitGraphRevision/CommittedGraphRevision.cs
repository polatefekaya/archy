namespace Archy.Features.Graph.CommitGraphRevision;

public sealed record CommittedGraphRevision(
    long Revision,
    string RunId,
    DateTimeOffset CommittedAtUtc,
    int NodeCount,
    int EdgeCount,
    int SymbolCount = 0,
    int InterfaceFingerprintCount = 0);
