using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

public interface IGraphRevisionCommitter
{
    ValueTask<Result<CommittedGraphRevision>> CommitAsync(
        WorkspaceStateLocation location,
        string runId,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        CancellationToken cancellationToken);

    ValueTask<Result<CommittedGraphRevision>> CommitAsync(
        WorkspaceStateLocation location,
        string runId,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<GraphSymbolFact> symbols,
        IReadOnlyList<InterfaceFingerprintFact> interfaceFingerprints,
        CancellationToken cancellationToken);
}
