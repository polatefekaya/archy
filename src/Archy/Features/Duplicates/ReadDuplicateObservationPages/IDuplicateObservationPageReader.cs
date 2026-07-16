using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ReadDuplicateObservationPages;

public interface IDuplicateObservationPageReader { ValueTask<Result<DuplicateObservationPage>> ReadAsync(WorkspaceStateLocation location, string findingId, int offset, int limit, CancellationToken cancellationToken); }
