using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Placement.ClusterRevisions;

public interface IClusterRevisionRepository
{
    ValueTask<Result<ClusterRevision>> RecordAsync(
        WorkspaceStateLocation location,
        ClusterRevisionFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<ClusterRevision>> GetAsync(
        WorkspaceStateLocation location,
        string clusterRevisionId,
        CancellationToken cancellationToken);
}
