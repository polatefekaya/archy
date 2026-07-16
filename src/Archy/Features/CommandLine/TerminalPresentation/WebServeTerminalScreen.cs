using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Web.RunLocalWebHost;

namespace Archy.Features.CommandLine.TerminalPresentation;

public static class WebServeTerminalScreen
{
    public static void Write(LocalWebHostOptions options, McpWorkspaceContext? workspace)
    {
        var url = $"http://{options.BindAddress}:{options.Port}/";
        TerminalCardWriter.WriteToStandardOutput(new TerminalCard(
            "ARCHY  ·  LOCAL ARCHITECTURE MAP",
            "Your repository graph is ready to explore.",
            [
                new TerminalDetail("Open", url),
                new TerminalDetail("Workspace", workspace is null ? "unavailable" : Path.GetFileName(workspace.RepositoryRoot)),
                new TerminalDetail("Graph state", workspace is null ? "Run archy workspace init" : "local state connected"),
                new TerminalDetail("Privacy", "loopback only · no network exposure"),
            ],
            "Press Ctrl+C to stop the local server."));
    }
}
