using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.SummaryBatches;

public interface ISummaryBatchRepository
{
    ValueTask<Result<SummaryBatch>> CreateAsync(
        WorkspaceStateLocation location,
        SummaryBatchFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<SummaryBatch>> GetAsync(
        WorkspaceStateLocation location,
        string summaryBatchId,
        CancellationToken cancellationToken);
}
