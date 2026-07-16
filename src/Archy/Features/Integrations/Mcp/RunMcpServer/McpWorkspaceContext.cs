using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed record McpWorkspaceContext(string RepositoryRoot, WorkspaceStateLocation StateLocation);
