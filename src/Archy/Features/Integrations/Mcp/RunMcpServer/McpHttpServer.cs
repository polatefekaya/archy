using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Optional, loopback-only Streamable HTTP endpoint. It is never started by the stdio command.</summary>
public sealed class McpHttpServer(
    McpWorkspaceContextFactory workspaceFactory,
    McpRequestRouter router,
    McpHttpServerOptions options)
{
    public async Task<int> RunAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var workspace = await workspaceFactory.CreateAsync(workspacePath, cancellationToken);
        using var listener = new TcpListener(options.BindAddress, options.Port);
        listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await HandleClientAsync(client, workspace, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            listener.Stop();
        }

        return 0;
    }

    private async Task HandleClientAsync(TcpClient client, McpWorkspaceContext? workspace, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        try
        {
            var request = await McpHttpRequestReader.ReadAsync(stream, cancellationToken);
            if (request is null)
            {
                return;
            }

            if (!IsAuthorized(request.Headers))
            {
                await McpHttpResponseWriter.WriteAsync(stream, 401, "{\"error\":\"Unauthorized\"}", false, cancellationToken);
                return;
            }

            if (request.Method != "POST")
            {
                await McpHttpResponseWriter.WriteAsync(stream, 405, "{\"error\":\"POST is required.\"}", false, cancellationToken);
                return;
            }

            if (request.Path != "/mcp")
            {
                await McpHttpResponseWriter.WriteAsync(stream, 404, "{\"error\":\"MCP endpoint was not found.\"}", false, cancellationToken);
                return;
            }

            var response = await router.RouteAsync(request.Body, workspace, cancellationToken);
            if (response is null)
            {
                await McpHttpResponseWriter.WriteAsync(stream, 200, "{}", AcceptsEventStream(request.Headers), cancellationToken);
                return;
            }

            await McpHttpResponseWriter.WriteAsync(stream, 200, response, AcceptsEventStream(request.Headers), cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            var status = exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase) ? 413 : 400;
            await McpHttpResponseWriter.WriteAsync(stream, status, "{\"error\":\"Invalid MCP HTTP request.\"}", false, cancellationToken);
        }
    }

    private bool IsAuthorized(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Authorization", out var authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(authorization[7..]);
        var expected = Encoding.UTF8.GetBytes(options.BearerToken);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static bool AcceptsEventStream(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Accept", out var accept)
        && accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
}
