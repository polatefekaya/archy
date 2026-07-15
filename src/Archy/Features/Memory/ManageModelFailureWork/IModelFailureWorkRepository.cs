using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.ManageModelFailureWork;

public interface IModelFailureWorkRepository
{
    ValueTask<Result<ModelFailureWorkItem>> EnqueueRetryableAsync(WorkspaceStateLocation location, ModelFailureWorkFact fact, CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<ModelFailureWorkItem>>> ListActiveAsync(WorkspaceStateLocation location, CancellationToken cancellationToken);

    ValueTask<Result<ModelFailureWorkItem>> SetStateAsync(WorkspaceStateLocation location, string workItemId, ModelFailureWorkState state, string reasonJson, CancellationToken cancellationToken);
}
