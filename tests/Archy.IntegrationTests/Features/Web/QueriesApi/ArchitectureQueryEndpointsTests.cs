using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.QueriesApi;

public sealed class ArchitectureQueryEndpointsTests
{
    [Fact]
    public async Task ExecutesDocumentedGrammarAndRejectsUnsupportedOrInvalidRevisionRequests()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        _ = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [GraphRevisionTestBuilder.Node("node:a", "a"), GraphRevisionTestBuilder.Node("node:b", "b")],
            [GraphRevisionTestBuilder.Edge("edge:a-b", "node:a", "node:b")]);
        using var provider = BuildServices();
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, provider, new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation), cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        _ = await GetWhenReadyAsync(client, cancellation.Token);

        var supported = await client.GetAsync("/api/v1/query?text=what%20does%20node%3Aa%20use%3F", cancellation.Token);
        var unsupported = await client.GetAsync("/api/v1/query?text=explain%20the%20architecture", cancellation.Token);
        var invalidRevision = await client.GetAsync("/api/v1/query?text=what%20uses%20node%3Aa%3F&revision=zero", cancellation.Token);
        using var body = JsonDocument.Parse(await supported.Content.ReadAsStreamAsync(cancellation.Token));

        Assert.Equal(HttpStatusCode.OK, supported.StatusCode);
        Assert.Equal("archy.graph-traversal/v1", body.RootElement.GetProperty("schema").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevision.StatusCode);

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceLockManager>(_ => new WorkspaceLockManager(TimeProvider.System));
        services.AddSingleton<IGraphTraversalReader, GraphTraversalReader>();
        return services.BuildServiceProvider();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> GetWhenReadyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try { return await client.GetStringAsync("/health", cancellationToken); }
            catch (HttpRequestException) when (attempt < 29) { await Task.Delay(100, cancellationToken); }
        }

        throw new InvalidOperationException("The local Archy host did not start in time.");
    }
}
