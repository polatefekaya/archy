using Archy.Features.Placement.AdviseSplitOrAppend;
using Archy.Features.Placement.ClusterRevisions;
using Archy.Features.Placement.ScorePlacementOverlap;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>
/// Provides advisory-only placement guidance from caller-declared facts and the latest persisted graph clusters.
/// It never writes source files, invokes a language server, or scans an unsaved proposed snippet.
/// </summary>
public sealed class SuggestPlacementMcpTool : IMcpTool
{
    public string Name => "suggest_placement";
    public string Description => "Return non-mutating placement and split-or-append advice for declared dependencies and module metrics.";
    public string InputSchemaJson => """
        {"type":"object","properties":{"dependencies":{"type":"array","minItems":1,"description":"Declared dependencies of the proposed responsibility.","items":{"type":"object","properties":{"targetStableId":{"type":"string","minLength":1},"confidence":{"type":"number","minimum":0,"maximum":1}},"required":["targetStableId","confidence"]}},"moduleMetrics":{"type":"object","description":"Optional metrics enabling split-or-append advice.","properties":{"moduleKey":{"type":"string","minLength":1},"memberCount":{"type":"integer","minimum":1},"cohesion":{"type":"number","minimum":0,"maximum":1},"fanIn":{"type":"number","minimum":0},"fanOut":{"type":"number","minimum":0},"suggestedFileName":{"type":"string","minLength":1}},"required":["moduleKey","memberCount","cohesion","fanIn","fanOut"]}},"required":["dependencies"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!TryReadDependencies(invocation, out var dependencies, out var dependencyError))
        {
            return McpToolResult.Failure(dependencyError!);
        }

        var clusters = await new LatestClusterRevisionLookup(new WorkspaceLockManager(TimeProvider.System))
            .ReadAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!clusters.IsSuccess)
        {
            return McpToolResult.Failure(clusters.Problem!.Message);
        }

        if (clusters.Value!.Count == 0)
        {
            return McpToolResult.Success(AbstentionResult("No cluster revision exists; placement advice abstains."));
        }

        var recommendation = new PlacementOverlapScorer().Score(
            dependencies,
            clusters.Value.Select(static cluster => new PlacementClusterCandidate(
                cluster.ClusterKey,
                cluster.Members.Select(static member => member.Target.StableId).ToArray())).ToArray());
        var splitAdvice = ReadSplitAdvice(invocation);

        return McpToolResult.Success(Result(recommendation, splitAdvice));
    }

    private static bool TryReadDependencies(
        McpToolInvocation invocation,
        out IReadOnlyList<PlacementDependency> dependencies,
        out string? error)
    {
        dependencies = [];
        error = null;
        if (!invocation.Arguments.TryGetProperty("dependencies", out var rawDependencies)
            || rawDependencies.ValueKind != System.Text.Json.JsonValueKind.Array
            || rawDependencies.GetArrayLength() == 0)
        {
            error = "dependencies must be a non-empty array.";
            return false;
        }

        var parsed = new List<PlacementDependency>();
        foreach (var item in rawDependencies.EnumerateArray())
        {
            if (!item.TryGetProperty("targetStableId", out var rawStableId)
                || string.IsNullOrWhiteSpace(rawStableId.GetString())
                || !item.TryGetProperty("confidence", out var rawConfidence)
                || !rawConfidence.TryGetDouble(out var confidence)
                || !double.IsFinite(confidence)
                || confidence is < 0 or > 1)
            {
                error = "Each dependency requires a non-empty targetStableId and confidence from 0 through 1.";
                return false;
            }

            parsed.Add(new PlacementDependency(rawStableId.GetString()!, confidence));
        }

        dependencies = parsed;
        return true;
    }

    private static SplitAppendAdvice ReadSplitAdvice(McpToolInvocation invocation)
    {
        if (!invocation.Arguments.TryGetProperty("moduleMetrics", out var metrics)
            || metrics.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new SplitAppendAdvice(
                SplitAppendAdviceKind.Abstain,
                "No declared module metrics were supplied, so split-or-append advice abstains.",
                null);
        }

        if (!metrics.TryGetProperty("moduleKey", out var moduleKey)
            || string.IsNullOrWhiteSpace(moduleKey.GetString())
            || !metrics.TryGetProperty("memberCount", out var memberCount)
            || !memberCount.TryGetInt32(out var members)
            || !metrics.TryGetProperty("cohesion", out var cohesion)
            || !cohesion.TryGetDouble(out var cohesionValue)
            || !metrics.TryGetProperty("fanIn", out var fanIn)
            || !fanIn.TryGetDouble(out var fanInValue)
            || !metrics.TryGetProperty("fanOut", out var fanOut)
            || !fanOut.TryGetDouble(out var fanOutValue))
        {
            return new SplitAppendAdvice(
                SplitAppendAdviceKind.Abstain,
                "moduleMetrics is incomplete, so split-or-append advice abstains.",
                null);
        }

        var suggestedFileName = metrics.TryGetProperty("suggestedFileName", out var fileName)
            ? fileName.GetString()
            : null;

        try
        {
            return new SplitAppendAdvisor(SplitAppendThresholds.Default).Advise(new SplitAppendInput(
                moduleKey.GetString()!,
                members,
                cohesionValue,
                fanInValue,
                fanOutValue,
                suggestedFileName));
        }
        catch (ArgumentException)
        {
            return new SplitAppendAdvice(
                SplitAppendAdviceKind.Abstain,
                "moduleMetrics is outside the supported range, so split-or-append advice abstains.",
                null);
        }
    }

    private static string Result(PlacementRecommendation recommendation, SplitAppendAdvice splitAdvice)
    {
        var candidates = string.Join(',', recommendation.Candidates.Select(static candidate =>
            $"{{\"clusterKey\":{McpJson.String(candidate.ClusterKey)},\"weightedOverlap\":{candidate.WeightedOverlap:R},\"matchedDependencyCount\":{candidate.MatchedDependencyCount}}}"));
        var suggestedFileName = splitAdvice.SuggestedFileName is null ? "null" : McpJson.String(splitAdvice.SuggestedFileName);

        return $"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(recommendation.Reason)}}}],\"structuredContent\":{{\"abstention\":{McpJson.Boolean(recommendation.IsAbstention)},\"placement\":{{\"reason\":{McpJson.String(recommendation.Reason)},\"candidates\":[{candidates}]}},\"splitOrAppend\":{{\"kind\":{McpJson.String(splitAdvice.Kind.ToString())},\"reason\":{McpJson.String(splitAdvice.Reason)},\"suggestedFileName\":{suggestedFileName}}},\"mutatedWorkingTree\":false}}}}";
    }

    private static string AbstentionResult(string reason) =>
        $"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(reason)}}}],\"structuredContent\":{{\"abstention\":true,\"placement\":{{\"reason\":{McpJson.String(reason)},\"candidates\":[]}},\"splitOrAppend\":{{\"kind\":\"Abstain\",\"reason\":{McpJson.String(reason)},\"suggestedFileName\":null}},\"mutatedWorkingTree\":false}}}}";
}
