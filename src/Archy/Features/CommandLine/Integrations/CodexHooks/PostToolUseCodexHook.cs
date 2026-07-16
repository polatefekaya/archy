using Archy.Features.Analysis.InventorySources;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Integrations.Codex.PostToolChangedPaths;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Mediator;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

/// <summary>Post-edit turn review. It reports after-the-fact deterministic findings and never represents itself as a write interceptor.</summary>
internal sealed class PostToolUseCodexHook(IMediator mediator, ICodexHookEventRecorder eventRecorder)
{
    public async Task<int> RunAsync(PostToolUseHookInput input, CancellationToken cancellationToken)
    {
        var workspace = await mediator.Send(new LocateWorkspaceCommand(input.WorkingDirectory), cancellationToken);
        if (!workspace.IsSuccess)
        {
            return 0;
        }

        var initializedWorkspace = await new McpWorkspaceContextFactory(mediator)
            .CreateAsync(workspace.Value!.RepositoryRoot, cancellationToken);
        if (initializedWorkspace is null)
        {
            await CodexHookResponseWriter.PostToolUseAsync(
                continueTurn: true,
                "Archy could not initialize workspace state after this edit; run `archy verify` before delivery.");
            return 0;
        }

        var effectiveConfiguration = await mediator.Send(
            new LoadEffectiveConfigurationQuery(workspace.Value!.RepositoryRoot, null, null),
            cancellationToken);
        if (!effectiveConfiguration.IsSuccess)
        {
            await CodexHookResponseWriter.PostToolUseAsync(
                continueTurn: true,
                "Archy could not load repository configuration after this edit; run `archy verify` before delivery.");
            return 0;
        }

        var scope = await SourceScopePolicy.CreateAsync(
            workspace.Value.RepositoryRoot,
            initializedWorkspace.StateLocation.StateDirectory,
            effectiveConfiguration.Value.Configuration.Scope,
            cancellationToken);
        if (!scope.IsSuccess)
        {
            await CodexHookResponseWriter.PostToolUseAsync(
                continueTurn: true,
                "Archy could not resolve changed-path scope after this edit; run `archy verify` before delivery.");
            return 0;
        }

        var hookPaths = ToRepositoryRelativePaths(workspace.Value.RepositoryRoot, input.ReportedPaths);
        var resolution = await new PostToolChangedPathResolver(new GitRepositoryCommitReader()).ResolveAsync(
            new PostToolChangedPathRequest(workspace.Value, scope.Value!, input.ToolKind, hookPaths, null, null, []),
            cancellationToken);
        if (!resolution.IsSuccess || resolution.Value!.CodePaths.Count == 0)
        {
            return 0;
        }

        var verification = await mediator.Send(
            new VerifyArchitectureCommand(workspace.Value.RepositoryRoot, null, null),
            cancellationToken);
        if (!verification.IsSuccess)
        {
            await CodexHookResponseWriter.PostToolUseAsync(
                continueTurn: true,
                "Archy could not complete deterministic post-edit verification; run `archy verify` before delivery.");
            return 0;
        }

        var introduced = verification.Value!.Baseline.Findings
            .Where(static finding => finding.Status == ArchitectureFindingStatus.Introduced)
            .Select(static finding => finding.Finding)
            .Take(5)
            .ToArray();
        if (introduced.Length == 0)
        {
            await RecordAsync(initializedWorkspace.StateLocation, input.SessionId, resolution.Value.CodePaths.Count, verification.Value.GraphRevision, false, [] , cancellationToken);
            await CodexHookResponseWriter.PostToolUseAsync(
                continueTurn: true,
                $"Archy reviewed {resolution.Value.CodePaths.Count} changed code path(s); no introduced deterministic violation was found.");
            return 0;
        }

        var remediation = string.Join("; ", introduced.Select(static finding => finding.Message));
        await RecordAsync(initializedWorkspace.StateLocation, input.SessionId, resolution.Value.CodePaths.Count, verification.Value.GraphRevision, true, introduced.Select(static finding => finding.Key).ToArray(), cancellationToken);
        await CodexHookResponseWriter.PostToolUseAsync(
            continueTurn: false,
            $"Archy found {introduced.Length} introduced deterministic violation(s) after this edit. Fix before continuing: {remediation}");
        return 0;
    }

    private static string[] ToRepositoryRelativePaths(string repositoryRoot, IReadOnlyList<string> paths)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var prefix = string.Concat(root, Path.DirectorySeparatorChar);
        return paths
            .Select(path => Path.IsPathRooted(path) ? Path.GetFullPath(path) : path)
            .Select(path => Path.IsPathRooted(path) && path.StartsWith(prefix, StringComparison.Ordinal)
                ? Path.GetRelativePath(root, path)
                : path)
            .Where(path => !Path.IsPathRooted(path))
            .Select(path => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'))
            .Where(path => !path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
            .Distinct(StringComparer.Ordinal)
            .Take(256)
            .ToArray();
    }

    private ValueTask RecordAsync(
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        string? sessionId,
        int changedCodePathCount,
        long graphRevision,
        bool turnStopped,
        IReadOnlyList<string> findingKeys,
        CancellationToken cancellationToken) =>
        eventRecorder.RecordValidationAsync(location, sessionId, new HookValidationEvent(changedCodePathCount, graphRevision, turnStopped, findingKeys), cancellationToken);
}
