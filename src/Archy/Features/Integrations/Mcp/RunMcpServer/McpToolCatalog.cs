namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpToolCatalog(IEnumerable<IMcpTool> tools)
{
    private readonly IReadOnlyDictionary<string, IMcpTool> byName = tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);

    public int Count => byName.Count;
    public string ListResultJson => $"{{\"tools\":[{string.Join(',', byName.Values.OrderBy(static tool => tool.Name, StringComparer.Ordinal).Select(static tool => $"{{\"name\":{McpJson.String(tool.Name)},\"description\":{McpJson.String(tool.Description)},\"inputSchema\":{tool.InputSchemaJson}}}"))}]}}";
    public bool TryGet(string name, out IMcpTool? tool) => byName.TryGetValue(name, out tool);
}
