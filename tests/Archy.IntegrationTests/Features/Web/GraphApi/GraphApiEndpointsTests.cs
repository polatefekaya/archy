using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Archy.Features.Graph.ReadGraphPage;
using Archy.Features.Graph.ExploreGraph;
using Archy.Features.Graph.RenderGraphMap;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.GraphApi;

public sealed class GraphApiEndpointsTests
{
    [Fact]
    public async Task ServesHistoricalPagesAndTraversalWithStrictInputAndOutputContracts()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var first = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [
                GraphRevisionTestBuilder.Node("node:a", "hash:a:v1"),
                GraphRevisionTestBuilder.Node("node:b", "hash:b:v1"),
                GraphRevisionTestBuilder.Node("node:c", "hash:c:v1"),
            ],
            [
                GraphRevisionTestBuilder.Edge("edge:a-b", "node:a", "node:b"),
                GraphRevisionTestBuilder.Edge("edge:b-c", "node:b", "node:c"),
            ]);
        _ = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:a", "hash:a:v2")], []);
        using var provider = BuildServices();
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(
            options!,
            provider,
            new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation),
            cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        _ = await GetWhenReadyAsync(client, "/health", cancellation.Token);
        var metadata = await GetJsonAsync(client, "/api/v1/graph/revision", cancellation.Token);
        var historicalNodes = await GetJsonAsync(client, $"/api/v1/graph/nodes?revision={first}&offset=1&limit=2", cancellation.Token);
        var dependencies = await GetJsonAsync(client, $"/api/v1/graph/dependencies/node:a?revision={first}&depth=6", cancellation.Token);
        var explorer = await GetJsonAsync(client, $"/api/v1/graph/explorer?revision={first}&focus=node:a&maxNodes=10", cancellation.Token);
        var search = await GetJsonAsync(client, $"/api/v1/graph/search?revision={first}&q=node", cancellation.Token);
        var map = await GetJsonAsync(client, $"/api/v1/graph/map?revision={first}", cancellation.Token);
        var badPage = await client.GetAsync("/api/v1/graph/nodes?limit=501", cancellation.Token);
        var missingRevision = await client.GetAsync("/api/v1/graph/edges?revision=999", cancellation.Token);

        Assert.Equal(2, metadata.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal(1, metadata.RootElement.GetProperty("nodeCount").GetInt32());
        Assert.Equal(first, historicalNodes.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal(3, historicalNodes.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(["node:b", "node:c"], historicalNodes.RootElement.GetProperty("items").EnumerateArray().Select(static node => node.GetProperty("stableId").GetString()));
        Assert.Equal(2, dependencies.RootElement.GetProperty("edges").GetArrayLength());
        Assert.Equal("archy.graph-explorer/v1", explorer.RootElement.GetProperty("schema").GetString());
        Assert.Equal("node:a", explorer.RootElement.GetProperty("centerStableId").GetString());
        Assert.Equal(3, explorer.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(2, explorer.RootElement.GetProperty("edges").GetArrayLength());
        Assert.Equal(["node:a", "node:b", "node:c"], search.RootElement.GetProperty("items").EnumerateArray().Select(static node => node.GetProperty("stableId").GetString()));
        Assert.Equal("archy.graph-map/v1", map.RootElement.GetProperty("schema").GetString());
        Assert.False(map.RootElement.GetProperty("isTruncated").GetBoolean());
        Assert.Equal(3, map.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(2, map.RootElement.GetProperty("edges").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRevision.StatusCode);
        Assert.Contains("\"schema\":\"archy.problem/v1\"", await badPage.Content.ReadAsStringAsync(cancellation.Token), StringComparison.Ordinal);

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceLockManager>(_ => new WorkspaceLockManager(TimeProvider.System));
        services.AddSingleton<IGraphRevisionPageReader, GraphRevisionPageReader>();
        services.AddSingleton<IGraphExplorerReader, GraphExplorerReader>();
        services.AddSingleton<IGraphMapReader, GraphMapReader>();
        services.AddSingleton<IGraphTraversalReader, GraphTraversalReader>();
        return services.BuildServiceProvider();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    private static async Task<string> GetWhenReadyAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                return await client.GetStringAsync(path, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 29)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new InvalidOperationException("The local Archy host did not start in time.");
    }
}
