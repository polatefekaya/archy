using Archy.Features.Web.RunLocalWebHost;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.CommandLine.TerminalPresentation;
using Mediator;

namespace Archy.Features.CommandLine.Web;

public static class WebCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.Ordinal))
        {
            return 64;
        }

        var port = LocalWebHostOptions.DefaultPort;
        var workspacePath = Environment.CurrentDirectory;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) return 64;
            switch (args[index])
            {
                case "--port" when int.TryParse(args[index + 1], out var parsedPort):
                    port = parsedPort;
                    break;
                case "--path" when !string.IsNullOrWhiteSpace(args[index + 1]):
                    workspacePath = args[index + 1];
                    break;
                default:
                    return 64;
            }
        }

        if (!LocalWebHostOptions.TryCreate(port, out var options)) return 64;
        var workspace = await new McpWorkspaceContextFactory(mediator).CreateAsync(workspacePath, cancellationToken);
        WebServeTerminalScreen.Write(options!, workspace);
        return await ArchyLocalWebHost.RunAsync(options!, serviceProvider, workspace, cancellationToken);
    }
}
