using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.ExplainReuseDecision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class WhyNotReuseMcpTool : IMcpTool
{
    public string Name => "why_not_reuse";
    public string Description => "Explain, using persisted structural evidence, whether to reuse, extend, or avoid one existing candidate. This is advisory and never relies on embeddings alone.";
    public string InputSchemaJson => """{"type":"object","properties":{"candidateStableId":{"type":"string","minLength":1},"proposedStableId":{"type":"string","minLength":1},"intent":{"type":"string","minLength":1,"maxLength":10000}},"required":["candidateStableId","intent"]}""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var candidate = invocation.Arguments.TryGetProperty("candidateStableId", out var rawCandidate) ? rawCandidate.GetString() : null;
        var proposed = invocation.Arguments.TryGetProperty("proposedStableId", out var rawProposed) ? rawProposed.GetString() : null;
        var intent = invocation.Arguments.TryGetProperty("intent", out var rawIntent) ? rawIntent.GetString() : null;
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(intent) || intent.Length > 10_000) return McpToolResult.Failure("candidateStableId and an intent of at most 10000 characters are required.");
        var active = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!active.IsSuccess) return McpToolResult.Failure(active.Problem!.Message);
        if (active.Value is null) return McpToolResult.Success("{\"content\":[{\"type\":\"text\",\"text\":\"No active graph revision exists; reuse guidance abstains.\"}],\"structuredContent\":{\"abstention\":true}}");
        var explanation = new ReuseDecisionExplainer().Explain(active.Value, new(candidate, proposed, intent));
        var support = Factors(explanation.SupportingFactors); var differences = Factors(explanation.DifferentiatingFactors);
        var text = McpJson.String($"Recommendation: {explanation.Recommendation}.");
        var checks = string.Join(',', explanation.SuggestedNextChecks.Select(McpJson.String));
        var json = $"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{active.Value.Revision},\"candidateStableId\":{McpJson.String(explanation.CandidateStableId)},\"recommendation\":{McpJson.String(explanation.Recommendation.ToString())},\"supportingFactors\":[{support}],\"differentiatingFactors\":[{differences}],\"suggestedNextChecks\":[{checks}],\"advisory\":true}}}}";
        return McpToolResult.Success(json);
    }

    private static string Factors(IReadOnlyList<ReuseFactor> factors) => string.Join(',', factors.Select(factor => $"{{\"id\":{McpJson.String(factor.Id)},\"detail\":{McpJson.String(factor.Detail)},\"deterministic\":{McpJson.Boolean(factor.Deterministic)}}}"));
}
