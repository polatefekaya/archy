using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;

public sealed record DotNetDependencyRegistrationFacts(
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<DotNetDependencyRegistrationDiagnostic> Diagnostics);

public sealed record DotNetDependencyRegistrationDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
