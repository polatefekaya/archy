using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Install;

public sealed class InstallGitHooksHandler(IGitHookLifecycle lifecycle)
    : IRequestHandler<InstallGitHooksCommand, Result<GitHookInstallation>>
{
    public ValueTask<Result<GitHookInstallation>> Handle(
        InstallGitHooksCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.InstallAsync(command.StartPath, cancellationToken);
    }
}
