using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Sessions.ReplayEventPages;
using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Web.StreamHookEvents;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.IntegrationTests.Features.Web.StreamHookEvents;

public sealed class HookEventWebSocketEndpointTests
{
    [Fact]
    public async Task ReconnectCursorReconstructsOrderedDurableAndLiveSessionEventsWithoutDuplicates()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        using var provider = BuildServices();
        var sessions = provider.GetRequiredService<IArchitectureSessionRepository>();
        var publisher = provider.GetRequiredService<IHookEventPublisher>();
        const string sessionId = "session-websocket";
        var started = await sessions.StartAsync(initialized.Value.StateLocation, new SessionStartFact(sessionId, "codex", "external:websocket", "codex_agent", "fixture", "{}"), CancellationToken.None);
        Assert.True(started.IsSuccess);
        var touched = await sessions.AppendEventAsync(initialized.Value.StateLocation, sessionId, new SessionEventFact(SessionEventKind.FileTouched, null, null, "{\"path\":\"Program.cs\"}"), CancellationToken.None);
        Assert.True(touched.IsSuccess);

        var port = ReservePort();
        Assert.True(LocalWebHostOptions.TryCreate(port, out var options));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = ArchyLocalWebHost.RunAsync(options!, provider, new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation), cancellation.Token);
        using var healthClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        _ = await GetWhenReadyAsync(healthClient, "/health", cancellation.Token);

        using var initial = new ClientWebSocket();
        await initial.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/api/v1/events?sessionId={sessionId}&after=0"), cancellation.Token);
        Assert.Equal(1, (await ReceiveAsync(initial, cancellation.Token)).RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal(2, (await ReceiveAsync(initial, cancellation.Token)).RootElement.GetProperty("sequence").GetInt32());

        var live = await sessions.AppendEventAsync(initialized.Value.StateLocation, sessionId, new SessionEventFact(SessionEventKind.ValidationCompleted, null, null, "{\"verdict\":\"allowed\"}"), CancellationToken.None);
        Assert.True(live.IsSuccess);
        publisher.Publish(new HookEventPublication(live.Value!, new HookValidationEvent(1, null, false, [])));
        Assert.Equal(3, (await ReceiveAsync(initial, cancellation.Token)).RootElement.GetProperty("sequence").GetInt32());
        initial.Abort();

        var afterDisconnect = await sessions.AppendEventAsync(initialized.Value.StateLocation, sessionId, new SessionEventFact(SessionEventKind.ValidationCompleted, null, null, "{\"verdict\":\"stopped\"}"), CancellationToken.None);
        Assert.True(afterDisconnect.IsSuccess);
        publisher.Publish(new HookEventPublication(afterDisconnect.Value!, new HookValidationEvent(1, null, true, ["finding-1"])));

        using var reconnected = new ClientWebSocket();
        await reconnected.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/api/v1/events?sessionId={sessionId}&after=3"), cancellation.Token);
        using var recovered = await ReceiveAsync(reconnected, cancellation.Token);
        Assert.Equal(4, recovered.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal("validation_completed", recovered.RootElement.GetProperty("kind").GetString());
        Assert.Equal("stopped", recovered.RootElement.GetProperty("payload").GetProperty("verdict").GetString());

        reconnected.Abort();
        await cancellation.CancelAsync();
        Assert.Equal(0, await run);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IWorkspaceLockManager, WorkspaceLockManager>();
        services.AddSingleton<ArchitectureSessionRepository>();
        services.AddSingleton<IArchitectureSessionRepository>(static provider => provider.GetRequiredService<ArchitectureSessionRepository>());
        services.AddSingleton<IArchitectureSessionEventReplayReader>(static provider => provider.GetRequiredService<ArchitectureSessionRepository>());
        services.AddSingleton<IHookEventPublisher, HookEventPublisher>();
        return services.BuildServiceProvider();
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer, cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            await stream.WriteAsync(buffer.AsMemory(0, received.Count), cancellationToken);
            Assert.True(stream.Length <= EventStreamJsonWriter.MaximumMessageBytes);
        }
        while (!received.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
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
