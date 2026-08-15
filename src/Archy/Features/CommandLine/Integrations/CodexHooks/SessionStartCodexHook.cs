using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Integrations.Codex.ComposeSessionArchitectureContext;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
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
        var sessionContext = await new SessionArchitectureContextComposer(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).ComposeAsync(workspace.StateLocation, cancellationToken);
        var preflight = await ReconcilePreflightContextAsync(repository, workspace.StateLocation, input.SessionId, current.GraphRevision, cancellationToken);
        var extra = sessionContext.IsSuccess ? $" {sessionContext.Value!.Text}" : " Persisted graph context is unavailable.";
        extra += await ComposeLayerRuleContextAsync(workspace.RepositoryRoot, cancellationToken);
        extra += preflight;
        var context = $"Archy session is active (graph revision {current.GraphRevision}). Current deterministic findings: {current.Baseline.CurrentFindings.Count}; introduced: {introduced}. Before changing boundaries, use `check_violation`, `get_module_rules`, and `suggest_placement`; `archy verify` remains the delivery gate.{extra}";
        await CodexHookResponseWriter.SessionStartContextAsync(context);
        return 0;
    }

    private async ValueTask<string> ComposeLayerRuleContextAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(repositoryRoot, null, null), cancellationToken);
        if (!configuration.IsSuccess || configuration.Value!.Configuration.Layers.Length == 0) return string.Empty;
        var layers = configuration.Value.Configuration.Layers.OrderBy(static layer => layer.Name, StringComparer.Ordinal).Take(8)
            .Select(layer => $"{layer.Name}→{(layer.MayDependOn.Length == 0 ? "none" : string.Join('/', layer.MayDependOn.Order(StringComparer.Ordinal)))}");
        return $" Effective layer rules: {string.Join(", ", layers)}.";
    }

    private static async ValueTask<string> ReconcilePreflightContextAsync(
        ArchitectureSessionRepository repository,
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        string sessionId,
        long graphRevision,
        CancellationToken cancellationToken)
    {
        var events = await repository.ListEventsAsync(location, sessionId, cancellationToken);
        if (!events.IsSuccess) return string.Empty;
        var preflight = events.Value!.LastOrDefault(@event => @event.Kind == SessionEventKind.PreflightContextRecorded);
        if (preflight is null) return string.Empty;
        if (preflight.GraphRevision == graphRevision) return $" Session preflight evidence is current for graph revision {graphRevision}.";
        var latestInvalidation = events.Value.LastOrDefault(@event => @event.Kind == SessionEventKind.PreflightContextInvalidated);
        if (latestInvalidation?.GraphRevision != graphRevision)
        {
            var payload = $"{{\"schema\":\"session-preflight-invalidation/v1\",\"preflightGraphRevision\":{(preflight.GraphRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")},\"activeGraphRevision\":{graphRevision}}}";
            _ = await repository.AppendEventAsync(location, sessionId, new SessionEventFact(SessionEventKind.PreflightContextInvalidated, graphRevision, null, payload), cancellationToken);
        }
        return $" Prior session preflight evidence from graph revision {preflight.GraphRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} is invalid for active graph revision {graphRevision}; run `preflight_change` again before editing.";
    }
}
