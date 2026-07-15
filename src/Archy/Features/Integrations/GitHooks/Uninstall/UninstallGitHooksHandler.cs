using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Uninstall;

public sealed class UninstallGitHooksHandler(IGitHookLifecycle lifecycle)
    : IRequestHandler<UninstallGitHooksCommand, Result<GitHookInstallation>>
{
    public ValueTask<Result<GitHookInstallation>> Handle(
        UninstallGitHooksCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.UninstallAsync(command.StartPath, cancellationToken);
    }
}
