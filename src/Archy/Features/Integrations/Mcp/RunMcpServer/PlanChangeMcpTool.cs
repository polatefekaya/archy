using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.PlanChange;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class PlanChangeMcpTool : IMcpTool
{
    public string Name => "plan_change";
    public string Description => "Create a bounded, advisory architecture change plan from persisted graph and similarity evidence.";
    public string InputSchemaJson => """{"type":"object","properties":{"description":{"type":"string","minLength":1,"maxLength":10000},"intendedFiles":{"type":"array","maxItems":50,"items":{"type":"string","minLength":1}},"targetStableId":{"type":"string","minLength":1},"intendedModule":{"type":"string","minLength":1},"snippet":{"type":"string","maxLength":100000}},"required":["description"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var description = invocation.Arguments.TryGetProperty("description", out var rawDescription) ? rawDescription.GetString() : null; if (string.IsNullOrWhiteSpace(description)) return McpToolResult.Failure("description is required.");
        var files = invocation.Arguments.TryGetProperty("intendedFiles", out var rawFiles) && rawFiles.ValueKind == System.Text.Json.JsonValueKind.Array ? rawFiles.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray() : null;
        var target = invocation.Arguments.TryGetProperty("targetStableId", out var rawTarget) ? rawTarget.GetString() : null; var module = invocation.Arguments.TryGetProperty("intendedModule", out var rawModule) ? rawModule.GetString() : null; var snippet = invocation.Arguments.TryGetProperty("snippet", out var rawSnippet) ? rawSnippet.GetString() : null;
        var locks = new WorkspaceLockManager(TimeProvider.System); var planner = new ChangePlanner(new GraphRevisionSnapshotReader(locks), new ImpactAnalyzer(new GraphTraversalReader(locks), new GraphRevisionSnapshotReader(locks)));
        var result = await planner.PlanAsync(invocation.Workspace.StateLocation, new(description, files, target, module, snippet), cancellationToken); if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var candidates = string.Join(',', result.Value!.Candidates.Select(CandidateJson));
        var existing = string.Join(',', result.Value.ExistingFiles.Select(McpJson.String)); var suggested = string.Join(',', result.Value.SuggestedNewFiles.Select(McpJson.String)); var decisions = string.Join(',', result.Value.DecisionIds.Select(McpJson.String)); var abstentions = string.Join(',', result.Value.Abstentions.Select(McpJson.String));
        var text = McpJson.String($"Change plan recommendation: {result.Value.Recommendation}.");
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{result.Value.GraphRevision},\"recommendation\":{McpJson.String(result.Value.Recommendation.ToString())},\"candidates\":[{candidates}],\"existingFiles\":[{existing}],\"suggestedNewFiles\":[{suggested}],\"decisionIds\":[{decisions}],\"validationSteps\":[{string.Join(',', result.Value.ValidationSteps.Select(McpJson.String))}],\"abstentions\":[{abstentions}],\"advisory\":true}}}}");
    }

    private static string CandidateJson(ChangePlanCandidate candidate)
    {
        var score = candidate.Candidate.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"stableId\":{McpJson.String(candidate.Candidate.StableId)},\"score\":{score},\"recommendation\":{McpJson.String(candidate.Explanation.Recommendation.ToString())}}}";
    }
}
