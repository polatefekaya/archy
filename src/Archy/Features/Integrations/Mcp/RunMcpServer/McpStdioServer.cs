using Archy.Features.CommandLine.TerminalPresentation;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Line-delimited JSON stdio transport. Protocol routing lives in <see cref="McpRequestRouter"/>.</summary>
public sealed class McpStdioServer(McpWorkspaceContextFactory workspaceFactory, McpRequestRouter router)
{
    public async Task<int> RunAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var workspaceResult = await workspaceFactory.CreateResultAsync(workspacePath, cancellationToken);
        if (!workspaceResult.IsSuccess)
        {
            McpStdioTerminalScreen.WriteUnavailable(workspacePath, workspaceResult.Problem!);
            return 2;
        }
        var workspace = workspaceResult.Value;

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
