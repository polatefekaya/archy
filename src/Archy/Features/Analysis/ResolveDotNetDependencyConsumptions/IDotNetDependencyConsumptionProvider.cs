using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;

public interface IDotNetDependencyConsumptionProvider
{
    ValueTask<Result<DotNetDependencyConsumptionFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> dependencyRegistrationEdges,
        CancellationToken cancellationToken);
}
