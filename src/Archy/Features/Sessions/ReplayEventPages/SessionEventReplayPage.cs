using Archy.Features.Sessions.ArchitectureSessions;

namespace Archy.Features.Sessions.ReplayEventPages;

/// <summary>One contiguous, bounded replay page after a client-owned sequence cursor.</summary>
public sealed record SessionEventReplayPage(
    IReadOnlyList<SessionEvent> Events,
    bool HasMore);
