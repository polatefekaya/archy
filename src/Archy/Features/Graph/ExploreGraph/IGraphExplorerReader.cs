using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.ExploreGraph;

public interface IGraphExplorerReader
{
    ValueTask<Result<GraphExplorerSnapshot>> ReadAsync(
        WorkspaceStateLocation location,
        GraphExplorerRequest request,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<GraphNodeFact>>> SearchAsync(
        WorkspaceStateLocation location,
        string query,
        long? revision,
        CancellationToken cancellationToken);
}
