using System.Text.Json;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal static class McpJson
{
    public static string String(string value) => $"\"{JsonEncodedText.Encode(value).ToString()}\"";
    public static string Boolean(bool value) => value ? "true" : "false";
}
