using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Decisions.ReadDecisionPages;

public interface IArchitectureDecisionPageReader
{
    ValueTask<Result<ArchitectureDecisionPage>> ReadAsync(WorkspaceStateLocation location, ArchitectureTarget target, int offset, int limit, CancellationToken cancellationToken);
}
