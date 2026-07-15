using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.GitHooks.Inspect;

/// <summary>Returns hook ownership and preservation state without changing the repository.</summary>
public sealed record InspectGitHooksCommand(string StartPath) : IRequest<Result<GitHookInstallation>>;
