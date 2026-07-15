using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Install;

/// <summary>Installs Archy's preserved pre-commit and pre-push wrappers for one repository.</summary>
public sealed record InstallGitHooksCommand(string StartPath) : IRequest<Result<GitHookInstallation>>;
