using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.ReadRepositoryCommit;

public interface IRepositoryCommitReader
{
    ValueTask<Result<string?>> ReadHeadAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken);
}
