using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ExtractCSharpSyntaxFacts;

public sealed record CSharpSyntaxFactBatch(
    bool IsComplete,
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<GraphSymbolFact> Symbols,
    IReadOnlyList<InterfaceFingerprintFact> InterfaceFingerprints,
    IReadOnlyList<CSharpSyntaxDiagnostic> Diagnostics);
