using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Mediator;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

/// <summary>Creates one attributed session and supplies concise, non-secret current architecture context.</summary>
internal sealed class SessionStartCodexHook(IMediator mediator)
{
    public async Task<int> RunAsync(CodexHookInput input, CancellationToken cancellationToken)
    {
        var workspace = await new McpWorkspaceContextFactory(mediator).CreateAsync(input.WorkingDirectory, cancellationToken);
        if (workspace is null)
        {
            await CodexHookResponseWriter.SessionStartContextAsync("Archy is unavailable for this workspace; continue normally and run `archy verify` before delivery.");
            return 0;
        }

        var repository = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var existing = await repository.GetAsync(workspace.StateLocation, input.SessionId, cancellationToken);
        if (!existing.IsSuccess)
        {
            var actorId = string.IsNullOrWhiteSpace(input.Model) ? "codex" : input.Model;
            var started = await repository.StartAsync(workspace.StateLocation, new SessionStartFact(
                input.SessionId,
                ClientKind: "codex",
                ExternalSessionId: input.SessionId,
                ActorKind: "agent",
                ActorId: actorId,
                StartPayloadJson: "{}"), cancellationToken);
            if (!started.IsSuccess)
            {
                await CodexHookResponseWriter.SessionStartContextAsync("Archy could not start its session record; continue normally and run `archy verify` before delivery.");
                return 0;
            }
        }

        var verification = await mediator.Send(
            new VerifyArchitectureCommand(workspace.RepositoryRoot, null, null),
            cancellationToken);
        if (!verification.IsSuccess)
        {
            await CodexHookResponseWriter.SessionStartContextAsync("Archy session is active. Architecture verification is currently unavailable; use `check_violation` before risky edits and `archy verify` before delivery.");
            return 0;
        }

        var current = verification.Value!;
        var introduced = current.Baseline.Findings.Count(static finding => finding.Status == ArchitectureFindingStatus.Introduced);
        var context = $"Archy session is active (graph revision {current.GraphRevision}). Current deterministic findings: {current.Baseline.CurrentFindings.Count}; introduced: {introduced}. Before changing boundaries, use `check_violation`, `get_module_rules`, and `suggest_placement`; `archy verify` remains the delivery gate.";
        await CodexHookResponseWriter.SessionStartContextAsync(context);
        return 0;
    }
}
