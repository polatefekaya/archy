namespace Archy.Features.Sessions.ArchitectureSessions;

public sealed record ArchitectureSession(
    string SessionId,
    string ClientKind,
    string? ExternalSessionId,
    string ActorKind,
    string ActorId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc)
{
    public bool IsEnded => EndedAtUtc is not null;
}
