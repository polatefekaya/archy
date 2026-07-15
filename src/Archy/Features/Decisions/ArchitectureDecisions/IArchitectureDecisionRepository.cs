using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Decisions.ArchitectureDecisions;

public interface IArchitectureDecisionRepository
{
    ValueTask<Result<ArchitectureDecision>> RecordAsync(
        WorkspaceStateLocation location,
        ArchitectureDecisionFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<ArchitectureDecision>>> ListForTargetAsync(
        WorkspaceStateLocation location,
        ArchitectureTarget target,
        CancellationToken cancellationToken);
}
