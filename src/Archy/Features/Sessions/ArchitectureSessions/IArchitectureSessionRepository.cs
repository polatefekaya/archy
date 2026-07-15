using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ArchitectureSessions;

public interface IArchitectureSessionRepository
{
    ValueTask<Result<ArchitectureSession>> StartAsync(
        WorkspaceStateLocation location,
        SessionStartFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<SessionEvent>> AppendEventAsync(
        WorkspaceStateLocation location,
        string sessionId,
        SessionEventFact fact,
        CancellationToken cancellationToken);

    ValueTask<Result<SessionEvent>> EndAsync(
        WorkspaceStateLocation location,
        string sessionId,
        string endPayloadJson,
        CancellationToken cancellationToken);

    ValueTask<Result<ArchitectureSession>> GetAsync(
        WorkspaceStateLocation location,
        string sessionId,
        CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<SessionEvent>>> ListEventsAsync(
        WorkspaceStateLocation location,
        string sessionId,
        CancellationToken cancellationToken);
}
