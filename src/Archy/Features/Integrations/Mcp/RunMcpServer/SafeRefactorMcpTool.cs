using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.PlanSafeRefactor;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class SafeRefactorMcpTool : IMcpTool
{
    public string Name => "safe_refactor";
    public string Description => "Produce a non-editing, checkpointed refactor migration plan from persisted graph facts.";
    public string InputSchemaJson => """{"type":"object","properties":{"stableId":{"type":"string","minLength":1},"intent":{"enum":["move","split","merge","rename","extract","replace"]},"destinationPath":{"type":"string","minLength":1}},"required":["stableId","intent"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var stableId = invocation.Arguments.TryGetProperty("stableId", out var rawId) ? rawId.GetString() : null; var intent = invocation.Arguments.TryGetProperty("intent", out var rawIntent) ? Parse(rawIntent.GetString()) : null; var destination = invocation.Arguments.TryGetProperty("destinationPath", out var rawPath) ? rawPath.GetString() : null;
        if (string.IsNullOrWhiteSpace(stableId) || intent is null) return McpToolResult.Failure("stableId and a supported intent are required.");
        var locks = new WorkspaceLockManager(TimeProvider.System); var planner = new SafeRefactorPlanner(new GraphRevisionSnapshotReader(locks), new ImpactAnalyzer(new GraphTraversalReader(locks), new GraphRevisionSnapshotReader(locks)));
        var result = await planner.PlanAsync(invocation.Workspace.StateLocation, new(stableId, intent.Value, destination), cancellationToken); if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var checkpoints = string.Join(',', result.Value!.Checkpoints.Select(checkpoint => $"{{\"phase\":{McpJson.String(checkpoint.Phase)},\"expectedState\":{McpJson.String(checkpoint.ExpectedState)},\"archyCheck\":{McpJson.String(checkpoint.ArchyCheck)}}}")); var abstentions = string.Join(',', result.Value.Abstentions.Select(McpJson.String));
        var text = McpJson.String("Safe refactor plan is advisory and does not edit code.");
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{result.Value.GraphRevision},\"intent\":{McpJson.String(result.Value.Intent.ToString())},\"checkpoints\":[{checkpoints}],\"abstentions\":[{abstentions}],\"advisory\":true}}}}");
    }
    private static RefactorIntent? Parse(string? value) => value?.ToLowerInvariant() switch { "move" => RefactorIntent.Move, "split" => RefactorIntent.Split, "merge" => RefactorIntent.Merge, "rename" => RefactorIntent.Rename, "extract" => RefactorIntent.Extract, "replace" => RefactorIntent.Replace, _ => null };
}
