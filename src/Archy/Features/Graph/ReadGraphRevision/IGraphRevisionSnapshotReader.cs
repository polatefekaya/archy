using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.ReadGraphRevision;

public interface IGraphRevisionSnapshotReader
{
    ValueTask<Result<GraphRevisionSnapshot?>> ReadActiveAsync(
        WorkspaceStateLocation location,
        CancellationToken cancellationToken);
}
