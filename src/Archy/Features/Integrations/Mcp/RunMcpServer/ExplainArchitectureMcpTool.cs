using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Planning.ExplainArchitecture;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class ExplainArchitectureMcpTool : IMcpTool
{
    public string Name => "explain_architecture";
    public string Description => "Explain an active graph target using persisted facts, decisions, and bounded history. Advisory context is explicitly labeled.";
    public string InputSchemaJson => """{"type":"object","properties":{"lookup":{"type":"string","minLength":1,"maxLength":320},"historyDepth":{"type":"integer","minimum":0,"maximum":20}},"required":["lookup"]}""";
    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var lookup = invocation.Arguments.TryGetProperty("lookup", out var raw) ? raw.GetString() : null;
        if (string.IsNullOrWhiteSpace(lookup)) return McpToolResult.Failure("lookup is required.");
        var depth = invocation.Arguments.TryGetProperty("historyDepth", out var rawDepth) && rawDepth.TryGetInt32(out var value) ? value : 3;
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var explainer = new ArchitectureExplainer(new GraphRevisionSnapshotReader(locks), new ArchitectureDecisionRepository(TimeProvider.System, locks));
        var result = await explainer.ExplainAsync(invocation.Workspace.StateLocation, new(lookup, depth), cancellationToken);
        if (!result.IsSuccess) return McpToolResult.Failure(result.Problem!.Message);
        var facts = string.Join(',', result.Value!.Facts.Select(fact => $"{{\"kind\":{McpJson.String(fact.Kind)},\"detail\":{McpJson.String(fact.Detail)},\"provenance\":{McpJson.String(fact.Provenance)}}}"));
        var decisions = string.Join(',', result.Value.Decisions.Select(decision => $"{{\"id\":{McpJson.String(decision.DecisionId)},\"type\":{McpJson.String(decision.Type)},\"resolution\":{McpJson.String(decision.Resolution)},\"note\":{McpJson.String(decision.Note ?? string.Empty)}}}"));
        var history = string.Join(',', result.Value.History.Select(item => $"{{\"revision\":{item.Revision},\"kind\":{McpJson.String(item.Kind)},\"detail\":{McpJson.String(item.Detail)},\"provenance\":{McpJson.String(item.Provenance)}}}"));
        var text = McpJson.String($"Architecture explanation for {result.Value.ResolvedStableId}.");
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{result.Value.GraphRevision},\"stableId\":{McpJson.String(result.Value.ResolvedStableId)},\"facts\":[{facts}],\"decisions\":[{decisions}],\"advisoryContext\":[],\"history\":[{history}],\"unknowns\":[{string.Join(',', result.Value.Unknowns.Select(McpJson.String))}],\"suggestedNextQuestions\":[{string.Join(',', result.Value.SuggestedNextQuestions.Select(McpJson.String))}],\"advisory\":true}}}}");
    }
}
