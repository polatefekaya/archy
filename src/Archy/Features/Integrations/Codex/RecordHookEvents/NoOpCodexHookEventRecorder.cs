using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Integrations.Codex.RecordHookEvents;

internal sealed class NoOpCodexHookEventRecorder : ICodexHookEventRecorder
{
    public ValueTask RecordValidationAsync(WorkspaceStateLocation location, string? sessionId, HookValidationEvent validationEvent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
