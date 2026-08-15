using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class ArchitectureIntelligenceMcpToolTests
{
    [Fact]
    public async Task ExplainArchitectureReturnsFactsAndRejectsMissingLookup()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:explain", "aaaaaaaaaaaaaaaa") with { DisplayName = "ExplainTarget", FilePath = "src/ExplainTarget.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        using var validArguments = JsonDocument.Parse("""{"lookup":"node:explain","historyDepth":1}""");
        using var invalidArguments = JsonDocument.Parse("{}" );
        var tool = new ExplainArchitectureMcpTool(); var context = new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation);
        var valid = await tool.ExecuteAsync(new("explain_architecture", validArguments.RootElement.Clone(), context), CancellationToken.None);
        var invalid = await tool.ExecuteAsync(new("explain_architecture", invalidArguments.RootElement.Clone(), context), CancellationToken.None);
        Assert.True(valid.IsSuccess); Assert.False(invalid.IsSuccess);
        using var payload = JsonDocument.Parse(valid.ResultJson!); var content = payload.RootElement.GetProperty("structuredContent"); Assert.Equal(node.StableId, content.GetProperty("stableId").GetString()); Assert.Contains(content.GetProperty("facts").EnumerateArray(), fact => fact.GetProperty("provenance").GetString() == "graph_nodes"); Assert.True(content.GetProperty("advisory").GetBoolean());
    }

    [Fact]
    public async Task ComposeChangeSummaryAbstainsForUnknownBaseAndReturnsDeterministicSummaryForKnownBase()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var first = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:first", "aaaaaaaaaaaaaaaa")]);
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:second", "bbbbbbbbbbbbbbbb")]);
        using var validArguments = JsonDocument.Parse($"{{\"baseRevision\":{first}}}");
        using var missingArguments = JsonDocument.Parse("""{"baseRevision":999}""");
        var tool = new ComposeChangeSummaryMcpTool(); var context = new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation);
        var valid = await tool.ExecuteAsync(new("compose_change_summary", validArguments.RootElement.Clone(), context), CancellationToken.None);
        var missing = await tool.ExecuteAsync(new("compose_change_summary", missingArguments.RootElement.Clone(), context), CancellationToken.None);
        Assert.True(valid.IsSuccess); Assert.True(missing.IsSuccess);
        using var validPayload = JsonDocument.Parse(valid.ResultJson!); Assert.False(validPayload.RootElement.GetProperty("structuredContent").GetProperty("abstained").GetBoolean()); Assert.NotEmpty(validPayload.RootElement.GetProperty("structuredContent").GetProperty("addedNodes").EnumerateArray());
        using var missingPayload = JsonDocument.Parse(missing.ResultJson!); Assert.True(missingPayload.RootElement.GetProperty("structuredContent").GetProperty("abstained").GetBoolean());
    }
}
