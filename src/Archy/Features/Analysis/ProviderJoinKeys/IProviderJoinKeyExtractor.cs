using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderJoinKeys;

public interface IProviderJoinKeyExtractor
{
    string ExtractorId { get; }

    ValueTask<Result<IReadOnlyList<ProviderJoinKey>>> ExtractAsync(
        ProviderSiteMatch site,
        CancellationToken cancellationToken);
}
