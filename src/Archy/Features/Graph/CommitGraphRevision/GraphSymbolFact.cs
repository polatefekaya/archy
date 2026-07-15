namespace Archy.Features.Graph.CommitGraphRevision;

public sealed record GraphSymbolFact(
    string SymbolId,
    string NodeStableId,
    string FullyQualifiedName,
    string Visibility,
    string NormalizedSignature,
    string ParameterMetadataJson,
    string ReturnMetadataJson,
    string SignatureHash);
