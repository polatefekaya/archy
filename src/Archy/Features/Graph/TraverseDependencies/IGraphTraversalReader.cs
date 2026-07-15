using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.TraverseDependencies;

public interface IGraphTraversalReader
{
    ValueTask<Result<GraphTraversal>> TraverseAsync(
        WorkspaceStateLocation location,
        GraphTraversalQuery query,
        CancellationToken cancellationToken);
}
