using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;

public interface IDotNetDependencyRegistrationProvider
{
    ValueTask<Result<DotNetDependencyRegistrationFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> nodes,
        CancellationToken cancellationToken);
}
