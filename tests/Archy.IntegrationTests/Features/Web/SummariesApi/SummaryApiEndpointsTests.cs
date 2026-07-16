using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Memory.ReadSummaryPages;
using Archy.Features.Memory.Summaries;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.SummariesApi;

public sealed class SummaryApiEndpointsTests
{
    [Fact]
    public async Task ServesBoundedSummaryHistoryAndRejectsOversizedPageRequests()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:summary", "hash")]);
        var appended = await new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).AppendAsync(initialized.Value.StateLocation, new SummaryVersionFact("summary:web", "graph_node", "node:summary", revision, null, "web summary", "diff", "fixture", "none", "{}", SummaryStaleness.Fresh), CancellationToken.None);
        Assert.True(appended.IsSuccess);
        using var provider = BuildServices();
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, provider, new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation), cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        _ = await GetWhenReadyAsync(client, "/health", cancellation.Token);
        var response = await client.GetAsync("/api/v1/summaries/summary:web?limit=1", cancellation.Token);
        var rejected = await client.GetAsync("/api/v1/summaries/summary:web?limit=51", cancellation.Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("archy.summary-page/v1", document.RootElement.GetProperty("schema").GetString());
        Assert.Equal("web summary", document.RootElement.GetProperty("items")[0].GetProperty("summaryText").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceLockManager>(_ => new WorkspaceLockManager(TimeProvider.System));
        services.AddSingleton<ISummaryVersionPageReader, SummaryVersionPageReader>();
        return services.BuildServiceProvider();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> GetWhenReadyAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try { return await client.GetStringAsync(path, cancellationToken); }
            catch (HttpRequestException) when (attempt < 29) { await Task.Delay(100, cancellationToken); }
        }

        throw new InvalidOperationException("The local Archy host did not start in time.");
    }
}
