using System.Net.Sockets;
using System.Text;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal static class McpHttpResponseWriter
{
    public static async Task WriteAsync(NetworkStream stream, int statusCode, string payload, bool streamEvents, CancellationToken cancellationToken)
    {
        var body = streamEvents ? $"event: message\ndata: {payload}\n\n" : payload;
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var reason = statusCode switch
        {
            200 => "OK",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            _ => "Bad Request",
        };
        var header = $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: {(streamEvents ? "text/event-stream" : "application/json")}; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
