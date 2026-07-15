namespace Archy.Features.Sessions.ArchitectureSessions;

public sealed record SessionStartFact(
    string SessionId,
    string ClientKind,
    string? ExternalSessionId,
    string ActorKind,
    string ActorId,
    string StartPayloadJson);
