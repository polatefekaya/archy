using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Archy.Features.CommandLine;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.GraphApi;
using Archy.Features.Web.StreamHookEvents;
using Archy.Features.Web.SummariesApi;
using Archy.Features.Web.DecisionsApi;
using Archy.Features.Web.DuplicatesApi;
using Archy.Features.Web.HealthApi;
using Archy.Features.Web.ClustersApi;
using Archy.Features.Web.QueriesApi;
using Archy.Features.Web.CapabilitiesApi;

namespace Archy.Features.Web.RunLocalWebHost;

/// <summary>Small AOT-safe, loopback-only host. Domain APIs are added as versioned slices on top of this boundary.</summary>
public static class ArchyLocalWebHost
{
    public static async Task<int> RunAsync(LocalWebHostOptions options, CancellationToken cancellationToken)
        => await RunAsync(options, services: null, workspace: null, cancellationToken);

    public static async Task<int> RunAsync(
        LocalWebHostOptions options,
        IServiceProvider? services,
        McpWorkspaceContext? workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        // The published SPA is copied beside the executable. Use an explicit physical provider
        // so the host behaves identically from a build output, a Native AOT publish directory,
        // and an integration-test working directory.
        var publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");
        builder.WebHost.ConfigureKestrel(server =>
        {
            // This is a local read-mostly API. Bound the parser before any endpoint sees data.
            server.Limits.MaxRequestBodySize = 256 * 1024;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        var application = builder.Build();
        application.UseExceptionHandler(handler => handler.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"schema\":\"archy.problem/v1\",\"code\":\"internal_error\",\"message\":\"The local Archy host could not complete this request.\"}");
        }));
        application.UseWebSockets();
        if (Directory.Exists(publishedWebRoot))
        {
            var staticFiles = new PhysicalFileProvider(publishedWebRoot);
            application.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticFiles });
            application.UseStaticFiles(new StaticFileOptions { FileProvider = staticFiles });
        }
        application.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";
            await next(context);
        });
        application.MapGet("/health", () => Json("{\"status\":\"ok\"}"));
        application.MapGet("/api/v1/status", () => Json($"{{\"name\":\"Archy\",\"version\":\"{ArchyProductMetadata.Version}\",\"bindAddress\":\"loopback\",\"apiVersion\":\"v1\"}}"));
        GraphApiEndpoints.Map(application, services, workspace);
        SummaryApiEndpoints.Map(application, services, workspace);
        DecisionApiEndpoints.Map(application, services, workspace);
        DuplicateApiEndpoints.Map(application, services, workspace);
        HealthApiEndpoints.Map(application, services, workspace);
        ClusterApiEndpoints.Map(application, services, workspace);
        ArchitectureQueryEndpoints.Map(application, services, workspace);
        CapabilityEndpoints.Map(application, services);
        HookEventWebSocketEndpoint.Map(application, services, workspace);
        // DefaultFiles does not rewrite the empty path under every slim-host/AOT hosting
        // combination. Map the document explicitly so `/` always starts the real SPA.
        // The embedded page remains a deliberate no-filesystem fallback for test hosts.
        var spaIndex = Path.Combine(publishedWebRoot, "index.html");
        application.MapGet("/", () => File.Exists(spaIndex)
            ? Results.File(spaIndex, "text/html; charset=utf-8")
            : Results.Content(WebUiAssets.IndexHtml, "text/html; charset=utf-8"));
        application.MapFallback(() => Results.NotFound());

        try
        {
            await application.RunAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            await application.DisposeAsync();
        }
    }

    private static IResult Json(string value) => Results.Content(value, "application/json; charset=utf-8");
}
