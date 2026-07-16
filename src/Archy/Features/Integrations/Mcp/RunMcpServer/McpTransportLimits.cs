namespace Archy.Features.Integrations.Mcp.RunMcpServer;

internal static class McpTransportLimits
{
    public const int MaximumRequestCharacters = 1_000_000;
    public const int MaximumHttpHeaderCharacters = 16_384;
}
