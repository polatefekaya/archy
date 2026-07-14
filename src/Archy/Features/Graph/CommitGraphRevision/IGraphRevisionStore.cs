using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

public interface IGraphRevisionStore
{
    ValueTask<Result<CommittedGraphRevision>> CommitAsync(WorkspaceStateLocation location, string runId, IReadOnlyList<GraphNodeFact> nodes, IReadOnlyList<GraphEdgeFact> edges, CancellationToken cancellationToken);
}
