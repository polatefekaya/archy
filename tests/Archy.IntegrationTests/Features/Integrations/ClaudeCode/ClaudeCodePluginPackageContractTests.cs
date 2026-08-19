using System.Text.Json;
using System.Text.RegularExpressions;

namespace Archy.IntegrationTests.Features.Integrations.ClaudeCode;

/// <summary>
/// Guards the Claude Code plugin package. These files are shipped, not built, so nothing else
/// fails when they drift away from the product they describe.
/// </summary>
public sealed partial class ClaudeCodePluginPackageContractTests
{
    [Fact]
    public void PluginManifestMatchesTheProductVersionAndDeclaresNoDuplicateComponentPaths()
    {
        var pluginRoot = PluginRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".claude-plugin", "plugin.json")));
        var root = manifest.RootElement;

        Assert.Equal("archy", root.GetProperty("name").GetString());
        Assert.Equal(ProductVersion(), root.GetProperty("version").GetString());
        Assert.Equal("MIT", root.GetProperty("license").GetString());

        // hooks/hooks.json and .mcp.json load from their standard locations. Declaring them again
        // in the manifest makes Claude Code reject the plugin as a duplicate hook source.
        Assert.False(root.TryGetProperty("hooks", out _));
        Assert.False(root.TryGetProperty("mcpServers", out _));
    }

    [Fact]
    public void McpServerStartsTheLocalStdioServerWithoutCredentials()
    {
        using var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginRoot(), ".mcp.json")));
        var server = mcp.RootElement.GetProperty("mcpServers").GetProperty("archy");

        Assert.Equal("archy", server.GetProperty("command").GetString());
        Assert.Equal(["mcp", "stdio"], server.GetProperty("args").EnumerateArray().Select(static item => item.GetString()));
        Assert.False(server.TryGetProperty("env", out _));
    }

    [Fact]
    public void HooksCoverEveryLifecycleEventUsingTheAgentNeutralCommand()
    {
        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginRoot(), "hooks", "hooks.json")));
        var root = hooks.RootElement.GetProperty("hooks");

        Assert.Equal(["PostToolUse", "SessionStart", "Stop"], root.EnumerateObject().Select(static hook => hook.Name).Order(StringComparer.Ordinal));
        Assert.Equal("archy agent-hook session-start", Command(root, "SessionStart"));
        Assert.Equal("archy agent-hook post-tool-use", Command(root, "PostToolUse"));
        Assert.Equal("archy agent-hook stop", Command(root, "Stop"));
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("Edit")]
    [InlineData("MultiEdit")]
    [InlineData("NotebookEdit")]
    [InlineData("Write")]
    public void PostToolUseMatcherSelectsEveryFileMutatingTool(string toolName)
    {
        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginRoot(), "hooks", "hooks.json")));
        var matcher = hooks.RootElement.GetProperty("hooks").GetProperty("PostToolUse")[0].GetProperty("matcher").GetString()!;

        // A tool the matcher skips is never checked: the write lands and the turn reads as clean.
        Assert.Matches($"^(?:{matcher})$", toolName);
    }

    [Fact]
    public void SkillTellsTheAgentWhenToReachForTheTools()
    {
        var skill = File.ReadAllText(Path.Combine(PluginRoot(), "skills", "architecture-preflight", "SKILL.md"));

        Assert.StartsWith("---", skill, StringComparison.Ordinal);
        Assert.Contains("name: architecture-preflight", skill, StringComparison.Ordinal);
        Assert.Contains("resolve_symbol", skill, StringComparison.Ordinal);
        Assert.Contains("advisory", skill, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceEntryResolvesToThePluginDirectoryAtTheDeclaredVersion()
    {
        var repositoryRoot = RepositoryRoot();
        using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, ".claude-plugin", "marketplace.json")));
        var entry = marketplace.RootElement.GetProperty("plugins").EnumerateArray().Single();

        Assert.Equal("archy", entry.GetProperty("name").GetString());
        Assert.Equal(ProductVersion(), entry.GetProperty("version").GetString());

        var source = entry.GetProperty("source").GetString()!;
        Assert.True(Directory.Exists(Path.GetFullPath(Path.Combine(repositoryRoot, source))), source);
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(repositoryRoot, source, ".claude-plugin", "plugin.json"))));
    }

    private static string Command(JsonElement hooks, string name) =>
        hooks.GetProperty(name)[0].GetProperty("hooks")[0].GetProperty("command").GetString()!;

    private static string PluginRoot() => Path.Combine(RepositoryRoot(), "plugins", "claude-code");

    private static string ProductVersion()
    {
        var version = ProductVersionPattern().Match(File.ReadAllText(Path.Combine(RepositoryRoot(), "eng", "Version.props"))).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(version));
        return version;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "Version.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "plugins", "claude-code")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root could not be found from the integration-test output directory.");
    }

    [GeneratedRegex("<ArchyVersion>([0-9]+\\.[0-9]+\\.[0-9]+)</ArchyVersion>", RegexOptions.CultureInvariant)]
    private static partial Regex ProductVersionPattern();
}
