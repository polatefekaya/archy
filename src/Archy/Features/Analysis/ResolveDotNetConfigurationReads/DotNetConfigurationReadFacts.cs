using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveDotNetConfigurationReads;

public sealed record DotNetConfigurationReadFacts(
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<DotNetConfigurationReadDiagnostic> Diagnostics);

public sealed record DotNetConfigurationReadDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
