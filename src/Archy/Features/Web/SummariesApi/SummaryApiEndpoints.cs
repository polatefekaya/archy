using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Memory.ReadSummaryPages;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.SummariesApi;

/// <summary>Read-only, bounded summary history endpoint.</summary>
public static class SummaryApiEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace) =>
        application.MapGet("/api/v1/summaries/{summaryId}", (string summaryId, HttpRequest request, CancellationToken cancellationToken) => GetAsync(summaryId, request, services, workspace, cancellationToken));

    private static async Task<GraphApiResult> GetAsync(string summaryId, HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(summaryId) || summaryId.Length > 512)
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Route parameter 'summaryId' must contain at most 512 characters.");
        }

        if (!TryPage(request, out var offset, out var limit, out var error))
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", error!);
        }

        if (workspace is null || services?.GetService<ISummaryVersionPageReader>() is not { } reader)
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have summary-read services for an initialized Archy workspace.");
        }

        var result = await reader.ReadAsync(workspace.StateLocation, summaryId, offset, limit, cancellationToken);
        return result.IsSuccess ? SummaryApiJsonWriter.Page(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static bool TryPage(HttpRequest request, out int offset, out int limit, out string? error)
    {
        offset = 0;
        limit = 20;
        error = null;
        if (request.Query.TryGetValue("offset", out var offsets) && (offsets.Count != 1 || !int.TryParse(offsets[0], out offset) || offset is < 0 or > 1_000_000))
        {
            error = "Query parameter 'offset' must be one integer between 0 and 1000000.";
            return false;
        }

        if (request.Query.TryGetValue("limit", out var limits) && (limits.Count != 1 || !int.TryParse(limits[0], out limit) || limit is < 1 or > 50))
        {
            error = "Query parameter 'limit' must be one integer between 1 and 50.";
            return false;
        }

        return true;
    }
}
