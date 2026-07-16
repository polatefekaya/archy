using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.RenderGraphMap;

public interface IGraphMapReader
{
    ValueTask<Result<GraphMapSnapshot>> ReadAsync(
        WorkspaceStateLocation location,
        long? revision,
        CancellationToken cancellationToken);
}
