using System.Text.Json;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed record McpToolInvocation(string Name, JsonElement Arguments, McpWorkspaceContext Workspace);
