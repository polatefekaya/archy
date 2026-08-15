using System.Text.Json;
using System.Text.RegularExpressions;

namespace Archy.IntegrationTests.Features.Integrations.Codex;

public sealed partial class PluginPackageContractTests
{
    [Fact]
    public void PluginManifestResolvesTheMcpHooksAndAssetsShippedToAgents()
    {
        var root = FindRepositoryRoot();
        var pluginRoot = Path.Combine(root, "plugins", "archy");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".codex-plugin", "plugin.json")));
        var manifestRoot = manifest.RootElement;
        Assert.Equal("archy", manifestRoot.GetProperty("name").GetString());

        var versionDocument = File.ReadAllText(Path.Combine(root, "eng", "Version.props"));
        var productVersion = ProductVersionPattern().Match(versionDocument).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(productVersion));
        Assert.Equal(productVersion, manifestRoot.GetProperty("version").GetString());

        AssertPluginPathExists(pluginRoot, manifestRoot.GetProperty("mcpServers").GetString());
        AssertPluginPathExists(pluginRoot, manifestRoot.GetProperty("hooks").GetString());
        var contract = manifestRoot.GetProperty("interface");
        AssertPluginPathExists(pluginRoot, contract.GetProperty("composerIcon").GetString());
        AssertPluginPathExists(pluginRoot, contract.GetProperty("logo").GetString());
        AssertPluginPathExists(pluginRoot, contract.GetProperty("logoDark").GetString());

        using var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".mcp.json")));
        var server = mcp.RootElement.GetProperty("mcpServers").GetProperty("archy");
        Assert.Equal("archy", server.GetProperty("command").GetString());
        Assert.Equal(["mcp", "stdio"], server.GetProperty("args").EnumerateArray().Select(static item => item.GetString()));
        Assert.Equal("prompt", server.GetProperty("default_tools_approval_mode").GetString());

        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginRoot, "hooks", "hooks.json")));
        var hookRoot = hooks.RootElement.GetProperty("hooks");
        Assert.Equal(["PostToolUse", "SessionStart", "Stop"], hookRoot.EnumerateObject().Select(static hook => hook.Name).Order(StringComparer.Ordinal));
        Assert.Equal("archy codex-hook session-start", Command(hookRoot, "SessionStart"));
        Assert.Equal("archy codex-hook post-tool-use", Command(hookRoot, "PostToolUse"));
        Assert.Equal("archy codex-hook stop", Command(hookRoot, "Stop"));

        var readme = File.ReadAllText(Path.Combine(pluginRoot, "README.md"));
        Assert.Contains("21 MCP tools", readme, StringComparison.Ordinal);
        Assert.Contains("SessionStart", readme, StringComparison.Ordinal);
        Assert.Contains("PostToolUse", readme, StringComparison.Ordinal);
        Assert.Contains("Stop", readme, StringComparison.Ordinal);
    }

    private static string Command(JsonElement hooks, string name) =>
        hooks.GetProperty(name)[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;

    private static void AssertPluginPathExists(string pluginRoot, string? relativePath)
    {
        Assert.False(string.IsNullOrWhiteSpace(relativePath));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(pluginRoot, relativePath!))), relativePath);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "Version.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "plugins", "archy")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be found from the integration-test output directory.");
    }

    [GeneratedRegex("<ArchyVersion>([0-9]+\\.[0-9]+\\.[0-9]+)</ArchyVersion>", RegexOptions.CultureInvariant)]
    private static partial Regex ProductVersionPattern();
}
