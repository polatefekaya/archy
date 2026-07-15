using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ScheduleProviderExecution;

public interface IProviderExecutionScheduler
{
    ValueTask<Result<ProviderExecutionBatch>> ExecuteAsync(
        ProviderExecutionSnapshot snapshot,
        IReadOnlyList<IProviderSiteMatcher> matchers,
        CancellationToken cancellationToken);
}
