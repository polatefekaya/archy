using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveDotNetMessageContracts;

public sealed record DotNetMessageContractFacts(
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<DotNetMessageContractDiagnostic> Diagnostics);

public sealed record DotNetMessageContractDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
