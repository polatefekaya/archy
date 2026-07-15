using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.Summaries;

public interface ISummaryRepository
{
    ValueTask<Result<SummaryVersion>> AppendAsync(
        WorkspaceStateLocation location,
        SummaryVersionFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<SummaryVersion>>> ListAsync(
        WorkspaceStateLocation location,
        string summaryId,
        CancellationToken cancellationToken);
}
