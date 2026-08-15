using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Planning.PlanChange;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Sessions.ArchitectureSessions;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Bounded advisory orchestration for an agent before it edits; it never blocks or observes future writes.</summary>
public sealed class PreflightChangeMcpTool(IMediator? mediator = null) : IMcpTool
{
    public string Name => "preflight_change";
    public string Description => "Advisory pre-edit architecture context: reuse candidates, bounded impact, risks, and verification steps. It cannot block writes.";
    public string InputSchemaJson => """{"type":"object","properties":{"description":{"type":"string","minLength":1,"maxLength":10000},"intendedFiles":{"type":"array","maxItems":50,"items":{"type":"string","minLength":1}},"targetStableIds":{"type":"array","maxItems":20,"items":{"type":"string","minLength":1}},"snippet":{"type":"string","maxLength":100000},"sessionId":{"type":"string","minLength":1,"maxLength":256,"description":"Optional active Archy session that receives bounded, revision-scoped preflight metadata."}},"required":["description"]}""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var description = invocation.Arguments.TryGetProperty("description", out var rawDescription) ? rawDescription.GetString() : null;
        if (string.IsNullOrWhiteSpace(description)) return McpToolResult.Failure("description is required.");
        var files = ReadStrings(invocation, "intendedFiles", 50); if (files is null) return McpToolResult.Failure("intendedFiles must be an array of at most 50 strings.");
        var targets = ReadStrings(invocation, "targetStableIds", 20); if (targets is null) return McpToolResult.Failure("targetStableIds must be an array of at most 20 strings.");
        var snippet = invocation.Arguments.TryGetProperty("snippet", out var rawSnippet) ? rawSnippet.GetString() : null;
        var sessionId = invocation.Arguments.TryGetProperty("sessionId", out var rawSessionId) ? rawSessionId.GetString() : null;
        if (sessionId is { Length: > 256 } || sessionId is { Length: 0 }) return McpToolResult.Failure("sessionId must be a non-empty identifier of at most 256 characters when supplied.");
        var locks = new WorkspaceLockManager(TimeProvider.System);
        var planner = new ChangePlanner(new GraphRevisionSnapshotReader(locks), new ImpactAnalyzer(new GraphTraversalReader(locks), new GraphRevisionSnapshotReader(locks)));
        var plan = await planner.PlanAsync(invocation.Workspace.StateLocation, new(description, files, targets.Length == 0 ? null : targets[0], null, snippet), cancellationToken);
        if (!plan.IsSuccess) return McpToolResult.Failure(plan.Problem!.Message);
        var candidates = string.Join(',', plan.Value!.Candidates.Select(candidate => $"{{\"stableId\":{McpJson.String(candidate.Candidate.StableId)},\"score\":{candidate.Candidate.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\"recommendation\":{McpJson.String(candidate.Explanation.Recommendation.ToString())}}}"));
        var risks = plan.Value.Impact is null ? string.Empty : string.Join(',', plan.Value.Impact.Risks.Select(risk => $"{{\"id\":{McpJson.String(risk.Id)},\"detail\":{McpJson.String(risk.Detail)},\"deterministic\":{McpJson.Boolean(risk.Deterministic)}}}"));
        var ruleEvidence = await ReadRuleEvidenceAsync(invocation, cancellationToken);
        if (!string.IsNullOrWhiteSpace(sessionId)) await RecordPreflightContextAsync(invocation, sessionId, plan.Value, cancellationToken);
        var hardRules = string.Join(',', ruleEvidence.Rules.Select(McpJson.String));
        var decisions = string.Join(',', plan.Value.DecisionIds.Select(McpJson.String));
        var abstentions = plan.Value.Abstentions.Concat(ruleEvidence.Abstentions).Distinct(StringComparer.Ordinal).Take(20).Select(McpJson.String);
        var text = McpJson.String("Preflight guidance is advisory and cannot block a write.");
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"graphRevision\":{plan.Value.GraphRevision},\"targetPlacement\":{{\"existingFiles\":[{string.Join(',', plan.Value.ExistingFiles.Select(McpJson.String))}],\"suggestedNewFiles\":[{string.Join(',', plan.Value.SuggestedNewFiles.Select(McpJson.String))}]}},\"reuseCandidates\":[{candidates}],\"hardRules\":[{hardRules}],\"impact\":{{\"available\":{McpJson.Boolean(plan.Value.Impact is not null)},\"risks\":[{risks}]}},\"risks\":[{risks}],\"decisions\":[{decisions}],\"verificationChecklist\":[{string.Join(',', plan.Value.ValidationSteps.Select(McpJson.String))}],\"abstentions\":[{string.Join(',', abstentions)}],\"advisory\":true}}}}");
    }

    private static async ValueTask RecordPreflightContextAsync(McpToolInvocation invocation, string sessionId, ChangePlan plan, CancellationToken cancellationToken)
    {
        // Persist only revision/provenance/counts: never the user description, snippet, source, or model output.
        var payload = $"{{\"schema\":\"session-preflight/v1\",\"graphRevision\":{plan.GraphRevision},\"candidateCount\":{plan.Candidates.Count},\"decisionCount\":{plan.DecisionIds.Count}}}";
        try
        {
            _ = await new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).AppendEventAsync(
                invocation.Workspace.StateLocation, sessionId,
                new SessionEventFact(SessionEventKind.PreflightContextRecorded, plan.GraphRevision == 0 ? null : plan.GraphRevision, null, payload), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* Advisory session telemetry must not make preflight fail. */ }
    }

    private async ValueTask<RuleEvidence> ReadRuleEvidenceAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (mediator is null) return new([], ["Configured layer and hard-rule evidence is unavailable because the MCP configuration service was not supplied."]);
        var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(invocation.Workspace.RepositoryRoot, null, null), cancellationToken);
        if (!configuration.IsSuccess) return new([], [$"Effective configuration could not be read: {configuration.Problem!.Message}"]);
        if (configuration.Value!.Configuration.Layers.Length == 0) return new([], ["No configured layers exist, so preflight cannot infer layer-direction rules."]);
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(invocation.Workspace.StateLocation, cancellationToken);
        if (!snapshot.IsSuccess || snapshot.Value is null) return new([], ["No active graph revision is available for hard-rule evaluation."]);
        var evaluator = new LayerDependencyRuleEvaluator(new LayerMembershipResolver(), new HardArchitectureEdgePolicy(), new ArchitectureCycleDetector());
        var evaluation = evaluator.Evaluate(snapshot.Value.Nodes, snapshot.Value.Edges, configuration.Value.Configuration.Layers, configuration.Value.Configuration.Enforcement);
        if (!evaluation.IsSuccess) return new([], [$"Hard-rule evaluation abstained: {evaluation.Problem!.Message}"]);
        var rules = evaluation.Value!.Violations.Select(violation => violation.Message)
            .Concat(evaluation.Value.Cycles.Select(cycle => $"Existing hard dependency cycle: {string.Join(" -> ", cycle.NodePath)}."))
            .Distinct(StringComparer.Ordinal).Take(20).ToArray();
        return rules.Length > 0
            ? new(rules, [])
            : new([], ["No existing deterministic hard-rule violation or cycle was found; proposed dependencies remain unknown until analysis."]);
    }

    private static string[]? ReadStrings(McpToolInvocation invocation, string property, int limit)
    {
        if (!invocation.Arguments.TryGetProperty(property, out var raw)) return [];
        if (raw.ValueKind != System.Text.Json.JsonValueKind.Array || raw.GetArrayLength() > limit) return null;
        var values = raw.EnumerateArray().Select(item => item.GetString()).ToArray();
        return values.Any(string.IsNullOrWhiteSpace) ? null : values.Cast<string>().ToArray();
    }

    private sealed record RuleEvidence(IReadOnlyList<string> Rules, IReadOnlyList<string> Abstentions);
}
