using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.ReadActiveGraphRevision;

public interface IActiveGraphRevisionReader
{
    ValueTask<Result<long?>> ReadAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken);
}
