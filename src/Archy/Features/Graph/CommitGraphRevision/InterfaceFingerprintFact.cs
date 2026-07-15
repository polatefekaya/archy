namespace Archy.Features.Graph.CommitGraphRevision;

public sealed record InterfaceFingerprintFact(
    string SymbolId,
    string FingerprintKind,
    string FingerprintHash,
    string NormalizedMembersJson);
