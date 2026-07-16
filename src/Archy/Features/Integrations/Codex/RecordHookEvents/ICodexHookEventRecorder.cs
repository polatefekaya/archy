using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

public interface ICodexHookEventRecorder
{
    ValueTask RecordValidationAsync(
        WorkspaceStateLocation location,
        string? sessionId,
        HookValidationEvent validationEvent,
        CancellationToken cancellationToken);
}
