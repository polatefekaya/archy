namespace Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;

/// <summary>Bounded, already-resolved architecture facts for the generated portion of AGENTS.md.</summary>
public sealed record AgentsManagedSectionInput(
    long GraphRevision,
    IReadOnlyList<string> LayerConventions,
    IReadOnlyList<string> Exceptions,
    IReadOnlyList<string> NextSessionNotes);
