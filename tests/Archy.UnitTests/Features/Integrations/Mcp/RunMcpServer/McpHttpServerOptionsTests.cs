using Archy.Features.Integrations.Mcp.RunMcpServer;
using System.Net;

namespace Archy.UnitTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpHttpServerOptionsTests
{
    [Theory]
    [InlineData(0, "abcdefghijklmnopqrstuvwxyz")]
    [InlineData(65536, "abcdefghijklmnopqrstuvwxyz")]
    [InlineData(8787, "too-short")]
    public void TryCreateRejectsAnUnsafeListenerConfiguration(int port, string token)
    {
        var created = McpHttpServerOptions.TryCreate(port, token, out var options, out var error);

        Assert.False(created);
        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryCreatePinsTheOptionalListenerToLoopback()
    {
        var created = McpHttpServerOptions.TryCreate(8787, "abcdefghijklmnopqrstuvwxyz", out var options, out _);

        Assert.True(created);
        Assert.Equal(IPAddress.Loopback, options!.BindAddress);
    }
}
