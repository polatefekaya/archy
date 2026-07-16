namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed record McpToolResult(bool IsSuccess, string ResultJson, string? ErrorMessage)
{
    public static McpToolResult Success(string resultJson) => new(true, resultJson, null);
    public static McpToolResult Failure(string message) => new(false, string.Empty, message);
}
