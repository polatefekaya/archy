using Archy.Features.Analysis.CSharpPatternTables;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.MatchCSharpPatternSites;

public interface ICSharpPatternSiteMatcher
{
    ValueTask<Result<IReadOnlyList<ProviderSiteMatch>>> MatchAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        CSharpPatternTable patternTable,
        CancellationToken cancellationToken);
}
