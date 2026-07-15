using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.DiscoverCSharpProjects;

public interface ICSharpProjectDiscovery
{
    ValueTask<Result<CSharpProjectMap>> DiscoverAsync(string repositoryRoot, CancellationToken cancellationToken);
}
