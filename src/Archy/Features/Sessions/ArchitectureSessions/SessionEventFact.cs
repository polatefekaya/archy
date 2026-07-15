using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ArchitectureSessions;

public sealed record SessionEventFact(
    SessionEventKind Kind,
    long? GraphRevision,
    ArchitectureTarget? Target,
    string PayloadJson);
