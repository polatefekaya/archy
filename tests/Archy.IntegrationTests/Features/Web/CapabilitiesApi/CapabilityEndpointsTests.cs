using System.Net;
using System.Net.Sockets;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Memory.ModelProviders.OpenAi;
using Archy.Features.Web.RunLocalWebHost;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.CapabilitiesApi;

public sealed class CapabilityEndpointsTests
{
    [Fact]
    public async Task ReportsReadinessWithoutLeakingTheModelCredential()
    {
        const string secret = "not-a-real-secret-value";
        var services = new ServiceCollection();
        services.AddSingleton<IOpenAiApiKeyProvider>(new FixedKeyProvider(secret));
        services.AddSingleton<IHookEventPublisher, HookEventPublisher>();
        using var provider = services.BuildServiceProvider();
        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, provider, workspace: null, cancellation.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        _ = await GetWhenReadyAsync(client, cancellation.Token);

        var response = await client.GetAsync("/api/v1/capabilities", cancellation.Token);
        var body = await response.Content.ReadAsStringAsync(cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"model\",\"ready\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"liveEvents\",\"ready\":true", body, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.DoesNotContain("Access-Control-Allow-Origin", response.Headers.Select(static header => header.Key));

        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
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

    private sealed class FixedKeyProvider(string value) : IOpenAiApiKeyProvider
    {
        public string? GetApiKey() => value;
    }
}
