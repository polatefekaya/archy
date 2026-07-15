namespace Archy.Features.Integrations.GitHooks;

/// <summary>The observable state of one supported hook in the resolved Git hook directory.</summary>
public sealed record GitHookState(
    GitHookKind Kind,
    string Path,
    bool IsManagedByArchy,
    bool HasPreservedHook);
