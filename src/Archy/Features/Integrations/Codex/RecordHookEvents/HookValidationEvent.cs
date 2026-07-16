namespace Archy.Features.Integrations.Codex.RecordHookEvents;

/// <summary>Stable event payload facts shared by hook persistence and future live UI publication.</summary>
public sealed record HookValidationEvent(
    int ChangedCodePathCount,
    long? GraphRevision,
    bool TurnStopped,
    IReadOnlyList<string> IntroducedFindingKeys);
