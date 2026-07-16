namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON Schema for the tool's <c>arguments</c> object, published through MCP <c>tools/list</c>.</summary>
    string InputSchemaJson { get; }
    ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken);
}
