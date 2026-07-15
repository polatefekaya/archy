using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Inspect;

public sealed class InspectGitHooksHandler(IGitHookLifecycle lifecycle)
    : IRequestHandler<InspectGitHooksCommand, Result<GitHookInstallation>>
{
    public ValueTask<Result<GitHookInstallation>> Handle(
        InspectGitHooksCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.InspectAsync(command.StartPath, cancellationToken);
    }
}
