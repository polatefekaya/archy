using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.GitHooks;

/// <summary>Installs, removes, and inspects the two local delivery-boundary hooks owned by Archy.</summary>
public interface IGitHookLifecycle
{
    ValueTask<Result<GitHookInstallation>> InstallAsync(string startPath, CancellationToken cancellationToken);

    ValueTask<Result<GitHookInstallation>> UninstallAsync(string startPath, CancellationToken cancellationToken);

    ValueTask<Result<GitHookInstallation>> InspectAsync(string startPath, CancellationToken cancellationToken);
}
