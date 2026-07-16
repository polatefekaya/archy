using Archy.Features.Integrations.Mcp.RunMcpServer;

namespace Archy.Features.CommandLine.TerminalPresentation;

public static class McpStdioTerminalScreen
{
    public static void WriteReady(McpWorkspaceContext workspace, int toolCount)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        TerminalCardWriter.WriteToStandardError(new TerminalCard(
            "ARCHY  ·  MCP READY",
            "Architecture tools are available to your agent.",
            [
                new TerminalDetail("Workspace", Path.GetFileName(workspace.RepositoryRoot)),
                new TerminalDetail("Transport", "stdio · protocol-safe"),
                new TerminalDetail("Tools", toolCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            "Operational messages use stderr; stdout is reserved for MCP JSON."));
    }

    public static void WriteUnavailable(string workspacePath) =>
        TerminalCardWriter.WriteToStandardError(new TerminalCard(
            "ARCHY  ·  MCP UNAVAILABLE",
            "The repository workspace could not be initialized.",
            [new TerminalDetail("Workspace", workspacePath)],
            "Run archy workspace init, then retry."));
}
