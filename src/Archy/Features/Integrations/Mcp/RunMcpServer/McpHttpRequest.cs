namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal sealed record McpHttpRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body);
