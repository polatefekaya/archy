using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.ReadSummaryPages;

public interface ISummaryVersionPageReader
{
    ValueTask<Result<SummaryVersionPage>> ReadAsync(
        WorkspaceStateLocation location,
        string summaryId,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
