using Archy.Features.Analysis.InventorySources;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderSiteMatches;

public interface IProviderSiteMatcher
{
    string ProviderId { get; }

    ValueTask<Result<IReadOnlyList<ProviderSiteMatch>>> MatchAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        CancellationToken cancellationToken);
}
