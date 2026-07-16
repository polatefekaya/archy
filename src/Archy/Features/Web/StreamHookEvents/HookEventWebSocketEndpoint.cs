using System.Net.WebSockets;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Sessions.ReplayEventPages;
using Archy.Features.Web.GraphApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Archy.Features.Web.StreamHookEvents;

/// <summary>Loopback WebSocket transport with durable per-session replay and live hook-event fan-out.</summary>
public static class HookEventWebSocketEndpoint
{
    private const int MaximumReplayEvents = 1_000;
    public static void Map(WebApplication application, IServiceProvider? services, McpWorkspaceContext? workspace)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.MapGet("/api/v1/events", (Delegate)(Func<HttpContext, Task>)(context => RunAsync(context, services, workspace)));
    }

    private static async Task RunAsync(HttpContext context, IServiceProvider? services, McpWorkspaceContext? workspace)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await GraphApiJsonWriter.Problem(StatusCodes.Status426UpgradeRequired, "websocket_required", "This endpoint requires a WebSocket upgrade.").ExecuteAsync(context);
            return;
        }

        if (!EventStreamRequest.TryParse(context.Request, out var request, out var error))
        {
            await GraphApiJsonWriter.Problem(StatusCodes.Status400BadRequest, "validation", error!).ExecuteAsync(context);
            return;
        }

        if (workspace is null
            || services?.GetService<IArchitectureSessionEventReplayReader>() is not { } replayReader
            || services.GetService<IHookEventPublisher>() is not { } publisher)
        {
            await GraphApiJsonWriter.Problem(StatusCodes.Status503ServiceUnavailable, "workspace_unavailable", "The local web host does not have event-stream services for an initialized Archy workspace.").ExecuteAsync(context);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var subscription = publisher.Subscribe();
        var replay = await replayReader.ReadAsync(workspace.StateLocation, request!.SessionId, request.AfterSequence, MaximumReplayEvents, context.RequestAborted);
        if (!replay.IsSuccess)
        {
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Unknown session", context.RequestAborted);
            return;
        }

        var deliveredSequence = request.AfterSequence;
        foreach (var sessionEvent in replay.Value!.Events)
        {
            await SendAsync(socket, sessionEvent, context.RequestAborted);
            deliveredSequence = sessionEvent.SequenceNumber;
        }

        if (replay.Value.HasMore)
        {
            await CloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "Replay page limit reached; reconnect with the last received sequence.", context.RequestAborted);
            return;
        }

        try
        {
            await foreach (var publication in subscription.Reader.ReadAllAsync(context.RequestAborted))
            {
                var sessionEvent = publication.SessionEvent;
                if (!string.Equals(sessionEvent.SessionId, request.SessionId, StringComparison.Ordinal)
                    || sessionEvent.SequenceNumber <= deliveredSequence)
                {
                    continue;
                }

                await SendAsync(socket, sessionEvent, context.RequestAborted);
                deliveredSequence = sessionEvent.SequenceNumber;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Disconnects are normal: the caller reconnects with its last delivered sequence.
        }
        catch (WebSocketException)
        {
            // A peer can disappear between durable catch-up and live delivery.
        }
    }

    private static Task SendAsync(WebSocket socket, SessionEvent sessionEvent, CancellationToken cancellationToken) =>
        socket.SendAsync(EventStreamJsonWriter.Serialize(sessionEvent), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(status, description, cancellationToken);
        }
    }
}
