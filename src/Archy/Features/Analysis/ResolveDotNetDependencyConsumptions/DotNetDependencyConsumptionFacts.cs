using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;

public sealed record DotNetDependencyConsumptionFacts(
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<DotNetDependencyConsumptionDiagnostic> Diagnostics);

public sealed record DotNetDependencyConsumptionDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
