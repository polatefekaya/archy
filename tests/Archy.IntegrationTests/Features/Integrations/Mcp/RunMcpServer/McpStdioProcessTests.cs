using System.Text.Json;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpStdioProcessTests
{
    [Fact]
    public async Task HostReportsTheActualWorkspaceFailureOnStandardError()
    {
        using var repository = TemporaryRepository.Create();
        var host = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
        var missing = Path.Combine(repository.Root, "does-not-exist");

        var result = await LocalProcess.RunAsync("dotnet", repository.Root, standardInput: null, host, "mcp", "stdio", missing);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("Reason", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("not_found", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasedHostKeepsStdoutProtocolCleanAndExecutesATool()
    {
        using var repository = TemporaryRepository.Create();
        var home = Path.Combine(Path.GetTempPath(), $"archy-mcp-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            var host = Path.Combine(AppContext.BaseDirectory, "Archy.dll");
            Assert.True(File.Exists(host));
            var input = """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"integration-test","version":"1.0"}}}
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_similar","arguments":{"query":"session lifecycle","limit":5}}}
                """;

            var result = await LocalProcess.RunAsync(
                "dotnet", repository.Root, input, new Dictionary<string, string> { ["HOME"] = home }, host, "mcp", "stdio");

            Assert.Equal(0, result.ExitCode);
            var responses = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            Assert.Equal(3, responses.Length);
            using (responses[0])
            {
                Assert.Equal("archy", responses[0].RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            }
            using (responses[1])
            {
                Assert.Equal(11, responses[1].RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
                Assert.Contains(responses[1].RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(), tool => tool.GetProperty("name").GetString() == "find_similar");
            }
            using (responses[2])
            {
                Assert.True(responses[2].RootElement.GetProperty("result").GetProperty("content").GetArrayLength() > 0);
            }
            Assert.Contains("MCP READY", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
    }
}
