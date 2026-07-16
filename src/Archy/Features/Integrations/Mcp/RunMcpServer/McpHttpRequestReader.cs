using System.Net.Sockets;
using System.Text;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal static class McpHttpRequestReader
{
    public static async ValueTask<McpHttpRequest?> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var requestLine = await ReadLineAsync(stream, McpTransportLimits.MaximumHttpHeaderCharacters, cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }

        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requestParts.Length != 3 || !requestParts[2].StartsWith("HTTP/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Malformed HTTP request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalHeaderLength = requestLine.Length;
        while (true)
        {
            var line = await ReadLineAsync(stream, McpTransportLimits.MaximumHttpHeaderCharacters, cancellationToken);
            if (line is null)
            {
                throw new InvalidDataException("Unexpected end of HTTP headers.");
            }

            totalHeaderLength += line.Length;
            if (totalHeaderLength > McpTransportLimits.MaximumHttpHeaderCharacters)
            {
                throw new InvalidDataException("HTTP headers exceed the MCP transport limit.");
            }

            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0 || headers.ContainsKey(line[..separator]))
            {
                throw new InvalidDataException("Malformed HTTP headers.");
            }

            headers.Add(line[..separator], line[(separator + 1)..].Trim());
        }

        if (!headers.TryGetValue("Content-Length", out var rawLength)
            || !int.TryParse(rawLength, out var contentLength)
            || contentLength is < 0 or > McpTransportLimits.MaximumRequestCharacters)
        {
            throw new InvalidDataException("A bounded Content-Length header is required.");
        }

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("Unexpected end of HTTP request body.");
            }

            offset += read;
        }

        return new McpHttpRequest(requestParts[0], requestParts[1], headers, new UTF8Encoding(false, true).GetString(body));
    }

    private static async ValueTask<string?> ReadLineAsync(NetworkStream stream, int maximumLength, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var next = new byte[1];
        while (buffer.Count <= maximumLength)
        {
            var read = await stream.ReadAsync(next, cancellationToken);
            if (read == 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString([.. buffer]);
            }

            if (next[0] == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == '\r')
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                return Encoding.ASCII.GetString([.. buffer]);
            }

            buffer.Add(next[0]);
        }

        throw new InvalidDataException("HTTP line exceeds the MCP transport limit.");
    }
}
