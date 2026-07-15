using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;

public sealed record JsonConfigurationDefinitionFacts(
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<JsonConfigurationDefinitionDiagnostic> Diagnostics);

public sealed record JsonConfigurationDefinitionDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
