using Archy.Features.Sessions.ArchitectureSessions;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

/// <summary>One durable session event made available to live local consumers after commit.</summary>
public record SessionEventPublication(SessionEvent SessionEvent, HookValidationEvent? ValidationEvent = null);
