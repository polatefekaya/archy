using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;

public interface IJsonConfigurationDefinitionProvider
{
    ValueTask<Result<JsonConfigurationDefinitionFacts>> ResolveAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<GraphNodeFact> knownConfigurationKeys,
        CancellationToken cancellationToken);
}
