using Archy.Features.Integrations.Codex.ComposeChangeSummary;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class ComposeChangeSummaryMcpTool : IMcpTool
{
    public string Name => "compose_change_summary";
    public string Description => "Create a bounded, Markdown-ready architecture graph delta from an explicit base revision. It never posts to a PR provider.";
    public string InputSchemaJson => """{"type":"object","properties":{"baseRevision":{"type":"integer","minimum":1}},"required":["baseRevision"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("baseRevision", out var raw) || !raw.TryGetInt64(out var baseRevision) || baseRevision < 1) return McpToolResult.Failure("baseRevision must be a positive graph revision.");
        var result = await new ArchitectureChangeSummaryComposer(new WorkspaceLockManager(TimeProvider.System)).ComposeAsync(invocation.Workspace.StateLocation, baseRevision, cancellationToken);
        if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var summary = result.Value!;
        var additions = string.Join(',', summary.AddedNodes.Select(DeltaJson)); var removals = string.Join(',', summary.RemovedNodes.Select(DeltaJson)); var changes = string.Join(',', summary.ChangedNodes.Select(DeltaJson));
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(summary.Markdown)}}}],\"structuredContent\":{{\"baseRevision\":{summary.BaseRevision},\"currentRevision\":{summary.CurrentRevision},\"addedNodes\":[{additions}],\"removedNodes\":[{removals}],\"changedNodes\":[{changes}],\"addedEdges\":{summary.AddedEdges},\"removedEdges\":{summary.RemovedEdges},\"abstained\":{McpJson.Boolean(summary.Abstained)},\"abstentionReason\":{McpJson.String(summary.AbstentionReason ?? string.Empty)},\"advisory\":true}}}}");
    }
    private static string DeltaJson(ArchitectureDelta delta) => $"{{\"stableId\":{McpJson.String(delta.StableId)},\"kind\":{McpJson.String(delta.Kind)},\"filePath\":{McpJson.String(delta.FilePath ?? string.Empty)},\"detail\":{McpJson.String(delta.Detail)}}}";
}
