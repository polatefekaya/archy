using System.Text.Json;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class WhyNotReuseMcpToolTests
{
    [Fact]
    public async Task ReturnsStructuredDeterministicReuseGuidance()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var source = GraphRevisionTestBuilder.Node("source", "aaaaaaaaaaaaaaaa"); var candidate = GraphRevisionTestBuilder.Node("candidate", "bbbbbbbbbbbbbbbb");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [source, candidate], symbols: [Symbol(source, "void()"), Symbol(candidate, "void()")] );
        using var arguments = JsonDocument.Parse("""{"candidateStableId":"candidate","proposedStableId":"source","intent":"candidate"}""");

        var result = await new WhyNotReuseMcpTool().ExecuteAsync(new("why_not_reuse", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);

        Assert.True(result.IsSuccess); using var response = JsonDocument.Parse(result.ResultJson!);
        var content = response.RootElement.GetProperty("structuredContent");
        Assert.Equal("Reuse", content.GetProperty("recommendation").GetString());
        Assert.True(content.GetProperty("advisory").GetBoolean());
    }

    private static GraphSymbolFact Symbol(GraphNodeFact node, string signature) => new($"symbol:{node.StableId}", node.StableId, node.CanonicalKey, "public", signature, "[]", "{}", $"hash:{node.StableId}");
}
