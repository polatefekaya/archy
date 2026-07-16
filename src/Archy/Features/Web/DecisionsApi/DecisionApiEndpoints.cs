using System.Text.Json;
using Archy.Features.Decisions.ReadDecisionPages;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.GraphApi;
using Archy.SharedKernel.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.DecisionsApi;

/// <summary>Read-only bounded decision pages keyed by one architecture target.</summary>
public static class DecisionApiEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace)
    {
        application.MapGet("/api/v1/decisions", (HttpRequest request, CancellationToken cancellationToken) =>
            GetAsync(request.Query["targetKind"].ToString(), request.Query["stableId"].ToString(), request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/decisions/{targetKind}/{stableId}", (string targetKind, string stableId, HttpRequest request, CancellationToken cancellationToken) => GetAsync(targetKind, stableId, request, services, workspace, cancellationToken));
    }

    private static async Task<GraphApiResult> GetAsync(string targetKind, string stableId, HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!TryTarget(targetKind, stableId, out var target) || !TryPage(request, out var offset, out var limit))
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "A valid target kind, stable ID, offset, and limit are required.");
        if (workspace is null || services?.GetService<IArchitectureDecisionPageReader>() is not { } reader)
            return GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have decision-read services for an initialized Archy workspace.");
        var result = await reader.ReadAsync(workspace.StateLocation, target!, offset, limit, cancellationToken);
        return result.IsSuccess ? Write(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static bool TryTarget(string kind, string stableId, out ArchitectureTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(stableId) || stableId.Length > 512) return false;
        var parsed = kind switch { "graph_node" => ArchitectureTargetKind.GraphNode, "graph_edge" => ArchitectureTargetKind.GraphEdge, "graph_symbol" => ArchitectureTargetKind.GraphSymbol, "rule" => ArchitectureTargetKind.Rule, "duplicate_finding" => ArchitectureTargetKind.DuplicateFinding, "placement_finding" => ArchitectureTargetKind.PlacementFinding, _ => (ArchitectureTargetKind?)null };
        if (parsed is null) return false; target = new ArchitectureTarget(parsed.Value, stableId); return true;
    }

    private static bool TryPage(HttpRequest request, out int offset, out int limit)
    {
        offset = 0; limit = 20;
        return (!request.Query.TryGetValue("offset", out var offsets) || offsets.Count == 1 && int.TryParse(offsets[0], out offset) && offset is >= 0 and <= 1_000_000)
            && (!request.Query.TryGetValue("limit", out var limits) || limits.Count == 1 && int.TryParse(limits[0], out limit) && limit is >= 1 and <= 100);
    }

    private static GraphApiResult Write(ArchitectureDecisionPage page)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("schema", "archy.decision-page/v1"); writer.WriteNumber("offset", page.Offset); writer.WriteNumber("limit", page.Limit); writer.WriteNumber("totalCount", page.TotalCount); writer.WriteStartArray("items");
            foreach (var decision in page.Items) { writer.WriteStartObject(); writer.WriteString("decisionId", decision.DecisionId); writer.WriteString("decisionType", decision.DecisionType); writer.WriteString("resolution", decision.Resolution.ToString().ToLowerInvariant()); writer.WriteString("note", decision.Note); writer.WriteString("actorKind", decision.ActorKind); writer.WriteString("actorId", decision.ActorId); if (decision.GraphRevision is null) writer.WriteNull("graphRevision"); else writer.WriteNumber("graphRevision", decision.GraphRevision.Value); writer.WriteString("occurredAtUtc", decision.OccurredAtUtc); writer.WriteStartArray("targets"); foreach (var target in decision.Targets) { writer.WriteStartObject(); writer.WriteString("kind", ArchitectureTargetCodec.ToStorageValue(target.Kind)); writer.WriteString("stableId", target.StableId); writer.WriteEndObject(); } writer.WriteEndArray(); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return new GraphApiResult(stream.ToArray(), StatusCodes.Status200OK);
    }
}
