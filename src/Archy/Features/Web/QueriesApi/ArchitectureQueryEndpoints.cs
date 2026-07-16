using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Queries.MapNaturalLanguageQuery;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.QueriesApi;

/// <summary>Executes only the documented deterministic query grammar against a declared graph revision.</summary>
public static class ArchitectureQueryEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace) =>
        application.MapGet("/api/v1/query", (HttpRequest request, CancellationToken cancellationToken) => RunAsync(request, services, workspace, cancellationToken));

    private static async Task<GraphApiResult> RunAsync(HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!request.Query.TryGetValue("text", out var text) || text.Count != 1 || text[0]!.Length > 2_048)
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Query parameter 'text' must contain one documented query of at most 2048 characters.");
        if (request.Query.TryGetValue("revision", out var revisions)
            && (revisions.Count != 1 || !long.TryParse(revisions[0], out var parsedRevision) || parsedRevision < 1))
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Query parameter 'revision' must be one positive integer when supplied.");
        var revision = request.Query.TryGetValue("revision", out revisions) ? long.Parse(revisions[0]!) : (long?)null;
        var mapped = ArchitectureQueryMapper.Map(text[0]!, revision);
        if (mapped.Intent is null) return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "unsupported_query", mapped.Clarification!);
        if (workspace is null || services?.GetService<IGraphTraversalReader>() is not { } reader)
            return GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have graph traversal services for an initialized Archy workspace.");
        var result = await reader.TraverseAsync(workspace.StateLocation, new GraphTraversalQuery(mapped.Intent.StableId, mapped.Intent.Direction, mapped.Intent.Revision, mapped.Intent.MaxDepth), cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Traversal(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }
}
