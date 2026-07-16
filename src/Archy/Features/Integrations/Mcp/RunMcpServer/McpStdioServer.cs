using Archy.Features.CommandLine.TerminalPresentation;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Line-delimited JSON stdio transport. Protocol routing lives in <see cref="McpRequestRouter"/>.</summary>
public sealed class McpStdioServer(McpWorkspaceContextFactory workspaceFactory, McpRequestRouter router)
{
    public async Task<int> RunAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var workspace = await workspaceFactory.CreateAsync(workspacePath, cancellationToken);
        if (workspace is null)
        {
            McpStdioTerminalScreen.WriteUnavailable(workspacePath);
            return 2;
        }

        McpStdioTerminalScreen.WriteReady(workspace, router.ToolCount);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return 0;
            }

            var response = await router.RouteAsync(line, workspace, cancellationToken);
            if (response is not null)
            {
                await Console.Out.WriteLineAsync(response);
            }
        }

        return 0;
    }
}
