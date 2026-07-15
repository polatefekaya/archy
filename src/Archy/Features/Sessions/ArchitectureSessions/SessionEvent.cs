using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ArchitectureSessions;

public sealed record SessionEvent(
    string EventId,
    string SessionId,
    int SequenceNumber,
    SessionEventKind Kind,
    long? GraphRevision,
    ArchitectureTarget? Target,
    string? DecisionId,
    string PayloadJson,
    DateTimeOffset OccurredAtUtc);
