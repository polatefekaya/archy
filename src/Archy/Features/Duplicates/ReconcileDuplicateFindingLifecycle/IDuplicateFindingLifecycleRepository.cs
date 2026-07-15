using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ReconcileDuplicateFindingLifecycle;

public interface IDuplicateFindingLifecycleRepository
{
    ValueTask<Result<IReadOnlyList<DuplicateFindingLifecycle>>> ReconcileAsync(WorkspaceStateLocation location, long graphRevision, IReadOnlyCollection<string> activeFindingIds, IReadOnlyDictionary<string, string> supersededBy, CancellationToken cancellationToken);
    ValueTask<Result<IReadOnlyList<DuplicateFindingLifecycle>>> ListActiveAtAsync(WorkspaceStateLocation location, long graphRevision, CancellationToken cancellationToken);
}
