using System.Net;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Explicit local-only configuration for the optional HTTP transport.</summary>
public sealed record McpHttpServerOptions(IPAddress BindAddress, int Port, string BearerToken)
{
    public static bool TryCreate(int port, string? bearerToken, out McpHttpServerOptions? options, out string? error)
    {
        options = null;
        error = null;
        if (port is < 1 or > 65535)
        {
            error = "MCP HTTP port must be from 1 through 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(bearerToken) || bearerToken.Length < 24)
        {
            error = "MCP HTTP requires an explicit bearer token of at least 24 characters.";
            return false;
        }

        options = new McpHttpServerOptions(IPAddress.Loopback, port, bearerToken);
        return true;
    }
}
