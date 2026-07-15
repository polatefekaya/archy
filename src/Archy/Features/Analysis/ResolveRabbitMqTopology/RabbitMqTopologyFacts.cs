using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.ResolveRabbitMqTopology;

public sealed record RabbitMqTopologyFacts(
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<RabbitMqTopologyDiagnostic> Diagnostics);

public sealed record RabbitMqTopologyDiagnostic(
    string RepositoryRelativePath,
    int Line,
    int Column,
    string Code,
    string Message);
