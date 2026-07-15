namespace Archy.Features.Integrations.GitHooks;

/// <summary>The Git delivery boundaries Archy manages without replacing user hook behavior.</summary>
public enum GitHookKind
{
    PreCommit,
    PrePush,
}
