using Archy.Features.Sessions.ArchitectureSessions;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

public sealed record HookEventPublication(SessionEvent SessionEvent, HookValidationEvent ValidationEvent)
    : SessionEventPublication(SessionEvent, ValidationEvent);
