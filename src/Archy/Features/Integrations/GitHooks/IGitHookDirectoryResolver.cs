using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.GitHooks;

internal interface IGitHookDirectoryResolver
{
    ValueTask<Result<GitHookDirectory>> ResolveAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken);
}
