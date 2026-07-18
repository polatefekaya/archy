using System.Text.Json;
using Archy.Features.CommandLine;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.UnitTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpRequestRouterTests
{
    [Fact]
    public async Task RouteReturnsTheCompleteCatalogAndEscapesToolMetadata()
    {
        var router = new McpRequestRouter(new McpToolCatalog([new FakeTool("quoted_tool", "A \"quoted\" description.")]));

        var response = await router.RouteAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Workspace(), CancellationToken.None);

        using var document = JsonDocument.Parse(response!);
        var tool = Assert.Single(document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray());
        Assert.Equal("quoted_tool", tool.GetProperty("name").GetString());
        Assert.Equal("A \"quoted\" description.", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RouteRejectsMalformedRequestsWithoutThrowing()
    {
        var router = new McpRequestRouter(new McpToolCatalog([]));

        var response = await router.RouteAsync("not-json", Workspace(), CancellationToken.None);

        using var document = JsonDocument.Parse(response!);
        Assert.Equal(-32700, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task InitializeReportsTheRunningArchyReleaseVersion()
    {
        var router = new McpRequestRouter(new McpToolCatalog([]));

        var response = await router.RouteAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""",
            Workspace(),
            CancellationToken.None);

        using var document = JsonDocument.Parse(response!);
        Assert.Equal(
            ArchyProductMetadata.Version,
            document.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("version").GetString());
    }

    private static McpWorkspaceContext Workspace() => new("/repository", new WorkspaceStateLocation("workspace", "/state", "/state/manifest", "/state/lock", "/state/archy.db"));

    private sealed class FakeTool(string name, string description) : IMcpTool
    {
        public string Name => name;
        public string Description => description;
        public string InputSchemaJson => """{"type":"object","properties":{}}""";
        public ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken) => ValueTask.FromResult(McpToolResult.Success("{}"));
    }
}
