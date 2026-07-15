using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.DuplicateFindings;

public interface IDuplicateFindingRepository
{
    ValueTask<Result<DuplicateFindingObservation>> RecordObservationAsync(
        WorkspaceStateLocation location,
        DuplicateFindingObservationFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<DuplicateFindingObservation>>> ListObservationsAsync(
        WorkspaceStateLocation location,
        string findingId,
        CancellationToken cancellationToken);

    ValueTask<Result<DuplicateResolutionLink>> LinkResolutionAsync(
        WorkspaceStateLocation location,
        string findingId,
        string decisionId,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<DuplicateResolutionLink>>> ListResolutionLinksAsync(
        WorkspaceStateLocation location,
        string findingId,
        CancellationToken cancellationToken);
}
