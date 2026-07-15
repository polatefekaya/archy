namespace Archy.Features.Integrations.GitHooks;

/// <summary>The repository-scoped result of inspecting or changing Archy's Git hook installation.</summary>
public sealed record GitHookInstallation(
    string RepositoryRoot,
    string HooksDirectory,
    bool UsesCustomHooksPath,
    IReadOnlyList<GitHookState> Hooks);
