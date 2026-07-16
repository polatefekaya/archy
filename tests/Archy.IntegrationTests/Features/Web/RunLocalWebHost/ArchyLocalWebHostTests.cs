using System.Net;
using System.Net.Sockets;
using Archy.Features.Web.RunLocalWebHost;

namespace Archy.IntegrationTests.Features.Web.RunLocalWebHost;

public sealed class ArchyLocalWebHostTests
{
    [Fact]
    public async Task RunServesHealthVersionedApiAndStaticShellOnlyOnLoopback()
    {
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        var health = await GetWhenReadyAsync(client, "/health", cancellation.Token);
        var status = await client.GetStringAsync("/api/v1/status", cancellation.Token);
        var shell = await client.GetStringAsync("/", cancellation.Token);

        Assert.Equal("{\"status\":\"ok\"}", health);
        Assert.Contains("\"apiVersion\":\"v1\"", status, StringComparison.Ordinal);
        Assert.Contains("<title>Archy</title>", shell, StringComparison.Ordinal);

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    [Fact]
    public async Task GraphRoutesFailClosedWhenTheHostHasNoInitializedWorkspace()
    {
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        _ = await GetWhenReadyAsync(client, "/health", cancellation.Token);
        var response = await client.GetAsync("/api/v1/graph/nodes?limit=1", cancellation.Token);
        var body = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"schema\":\"archy.problem/v1\",\"code\":\"workspace_unavailable\",\"message\":\"The local web host does not have an initialized Archy workspace.\"}", body);

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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
