using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Health.HealthSnapshots;

public interface IHealthSnapshotRepository
{
    ValueTask<Result<HealthSnapshot>> RecordAsync(
        WorkspaceStateLocation location,
        HealthSnapshotFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<HealthSnapshot>> GetAsync(
        WorkspaceStateLocation location,
        string healthSnapshotId,
        CancellationToken cancellationToken);
}
