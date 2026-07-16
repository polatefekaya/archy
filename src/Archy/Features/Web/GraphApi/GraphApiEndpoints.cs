using Archy.Features.Graph.ReadGraphPage;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Graph.ExploreGraph;
using Archy.Features.Graph.RenderGraphMap;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.GraphApi;

/// <summary>Versioned, read-only graph routes over exactly one initialized workspace.</summary>
public static class GraphApiEndpoints
{
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.MapGet("/api/v1/graph/revision", (Delegate)(Func<HttpContext, Task<GraphApiResult>>)(context => GetRevisionAsync(services, workspace, context.RequestAborted)));
        application.MapGet("/api/v1/graph/nodes", (HttpRequest request, CancellationToken cancellationToken) => GetPageAsync(request, GraphRevisionFactKind.Nodes, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/edges", (HttpRequest request, CancellationToken cancellationToken) => GetPageAsync(request, GraphRevisionFactKind.Edges, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/explorer", (HttpRequest request, CancellationToken cancellationToken) => ExploreAsync(request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/map", (HttpRequest request, CancellationToken cancellationToken) => MapAsync(request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/search", (HttpRequest request, CancellationToken cancellationToken) => SearchAsync(request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/traverse", (HttpRequest request, CancellationToken cancellationToken) => TraverseQueryAsync(request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/dependents/{stableId}", (string stableId, HttpRequest request, CancellationToken cancellationToken) => TraverseAsync(stableId, GraphTraversalDirection.Dependents, request, services, workspace, cancellationToken));
        application.MapGet("/api/v1/graph/dependencies/{stableId}", (string stableId, HttpRequest request, CancellationToken cancellationToken) => TraverseAsync(stableId, GraphTraversalDirection.Dependencies, request, services, workspace, cancellationToken));
    }

    private static async Task<GraphApiResult> GetRevisionAsync(IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!TryGetReader(services, workspace, out var reader, out var unavailable)) return unavailable!;
        var result = await reader!.ReadMetadataAsync(workspace!.StateLocation, revision: null, cancellationToken);
        if (!result.IsSuccess) return GraphApiJsonWriter.Problem(result.Problem!);
        return result.Value is null
            ? GraphApiJsonWriter.Problem(StatusCodes.Status404NotFound, "not_found", "The workspace has no committed graph revision.")
            : GraphApiJsonWriter.Metadata(result.Value);
    }

    private static async Task<GraphApiResult> GetPageAsync(HttpRequest request, GraphRevisionFactKind kind, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!GraphApiRequestParser.TryParsePage(request, out var revision, out var offset, out var limit, out var error))
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", error!);
        }

        if (!TryGetReader(services, workspace, out var reader, out var unavailable)) return unavailable!;
        var result = await reader!.ReadPageAsync(workspace!.StateLocation, new GraphRevisionPageQuery(kind, revision, offset, limit), cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Page(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static async Task<GraphApiResult> TraverseAsync(string stableId, GraphTraversalDirection direction, HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stableId) || stableId.Length > 1024)
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Route parameter 'stableId' must contain at most 1024 characters.");
        }

        if (!GraphApiRequestParser.TryParseTraversal(request, out var revision, out var depth, out var error))
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", error!);
        }

        if (workspace is null || services?.GetService<IGraphTraversalReader>() is not { } reader)
        {
            return GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have an initialized Archy workspace.");
        }

        var result = await reader.TraverseAsync(workspace.StateLocation, new GraphTraversalQuery(stableId, direction, revision, depth), cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Traversal(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static async Task<GraphApiResult> TraverseQueryAsync(HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        var stableId = request.Query["stableId"].ToString();
        var direction = request.Query["direction"].ToString() switch
        {
            "dependencies" => GraphTraversalDirection.Dependencies,
            "dependents" => GraphTraversalDirection.Dependents,
            _ => (GraphTraversalDirection?)null,
        };
        return direction is null
            ? GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Query parameter 'direction' must be dependencies or dependents.")
            : await TraverseAsync(stableId, direction.Value, request, services, workspace, cancellationToken);
    }

    private static async Task<GraphApiResult> ExploreAsync(HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!TryGetExplorer(services, workspace, out var reader, out var unavailable)) return unavailable!;
        if (!TryParseExplorerRequest(request, out var explorerRequest, out var error))
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", error!);
        var result = await reader!.ReadAsync(workspace!.StateLocation, explorerRequest!, cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Explorer(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static async Task<GraphApiResult> SearchAsync(HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!TryGetExplorer(services, workspace, out var reader, out var unavailable)) return unavailable!;
        var query = request.Query["q"].ToString();
        var revision = TryReadOptionalRevision(request, out var parsedRevision) ? parsedRevision : null;
        if (revision is null && request.Query.ContainsKey("revision"))
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Query parameter 'revision' must be a positive integer.");
        var result = await reader!.SearchAsync(workspace!.StateLocation, query, revision, cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Search(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static async Task<GraphApiResult> MapAsync(HttpRequest request, IServiceProvider? services, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        if (!TryGetMapReader(services, workspace, out var reader, out var unavailable)) return unavailable!;
        var revision = TryReadOptionalRevision(request, out var parsedRevision) ? parsedRevision : null;
        if (revision is null && request.Query.ContainsKey("revision"))
            return GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", "Query parameter 'revision' must be a positive integer.");
        var result = await reader!.ReadAsync(workspace!.StateLocation, revision, cancellationToken);
        return result.IsSuccess ? GraphApiJsonWriter.Map(result.Value!) : GraphApiJsonWriter.Problem(result.Problem!);
    }

    private static bool TryGetReader(IServiceProvider? services, McpWorkspaceContext? workspace, out IGraphRevisionPageReader? reader, out GraphApiResult? unavailable)
    {
        reader = services?.GetService<IGraphRevisionPageReader>();
        unavailable = null;
        if (workspace is not null && reader is not null) return true;
        unavailable = GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have an initialized Archy workspace.");
        return false;
    }

    private static bool TryGetExplorer(IServiceProvider? services, McpWorkspaceContext? workspace, out IGraphExplorerReader? reader, out GraphApiResult? unavailable)
    {
        reader = services?.GetService<IGraphExplorerReader>();
        unavailable = null;
        if (workspace is not null && reader is not null) return true;
        unavailable = GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have graph exploration services for an initialized Archy workspace.");
        return false;
    }

    private static bool TryGetMapReader(IServiceProvider? services, McpWorkspaceContext? workspace, out IGraphMapReader? reader, out GraphApiResult? unavailable)
    {
        reader = services?.GetService<IGraphMapReader>();
        unavailable = null;
        if (workspace is not null && reader is not null) return true;
        unavailable = GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have graph map services for an initialized Archy workspace.");
        return false;
    }

    private static bool TryParseExplorerRequest(HttpRequest request, out GraphExplorerRequest? explorerRequest, out string? error)
    {
        explorerRequest = null;
        error = null;
        if (!TryReadOptionalRevision(request, out var revision))
        {
            error = "Query parameter 'revision' must be a positive integer.";
            return false;
        }
        var focus = request.Query["focus"].ToString();
        if (focus.Length > 1024)
        {
            error = "Query parameter 'focus' must contain at most 1024 characters.";
            return false;
        }
        var maximumNodes = 72;
        if (request.Query.TryGetValue("maxNodes", out var values) && (values.Count != 1 || !int.TryParse(values[0], out maximumNodes) || maximumNodes is < 1 or > 120))
        {
            error = "Query parameter 'maxNodes' must be between 1 and 120.";
            return false;
        }
        explorerRequest = new GraphExplorerRequest(string.IsNullOrWhiteSpace(focus) ? null : focus, revision, maximumNodes);
        return true;
    }

    private static bool TryReadOptionalRevision(HttpRequest request, out long? revision)
    {
        revision = null;
        if (!request.Query.TryGetValue("revision", out var values)) return true;
        if (values.Count != 1 || !long.TryParse(values[0], out var parsed) || parsed < 1) return false;
        revision = parsed;
        return true;
    }
}
