using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Sessions.ReplayEventPages;

public interface IArchitectureSessionEventReplayReader
{
    ValueTask<Result<SessionEventReplayPage>> ReadAsync(
        WorkspaceStateLocation location,
        string sessionId,
        int afterSequence,
        int maximumEvents,
        CancellationToken cancellationToken);
}
