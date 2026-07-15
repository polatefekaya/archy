using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Uninstall;

/// <summary>Removes only Archy-managed wrappers and restores any preserved original hooks.</summary>
public sealed record UninstallGitHooksCommand(string StartPath) : IRequest<Result<GitHookInstallation>>;
