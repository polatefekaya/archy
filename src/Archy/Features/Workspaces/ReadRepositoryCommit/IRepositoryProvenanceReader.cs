using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.ReadRepositoryCommit;

public interface IRepositoryProvenanceReader
{
    ValueTask<Result<RepositoryProvenance>> ReadAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken);
}
