using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class PreflightChangeMcpToolTests
{
    [Fact]
    public async Task ReturnsAdvisoryBoundedPlanAndRejectsTraversal()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Sessions.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        using var validArguments = JsonDocument.Parse("""{"description":"create session","intendedFiles":["src/Sessions.cs"],"targetStableIds":["node:session"]}""");
        using var invalidArguments = JsonDocument.Parse("""{"description":"create session","intendedFiles":["../escape.cs"]}""");
        var tool = new PreflightChangeMcpTool(); var context = new McpWorkspaceContext(fixture.Repository.Root, initialized.Value.StateLocation);
        var valid = await tool.ExecuteAsync(new("preflight_change", validArguments.RootElement.Clone(), context), CancellationToken.None);
        var invalid = await tool.ExecuteAsync(new("preflight_change", invalidArguments.RootElement.Clone(), context), CancellationToken.None);
        Assert.True(valid.IsSuccess); Assert.False(invalid.IsSuccess);
        using var response = JsonDocument.Parse(valid.ResultJson!); var content = response.RootElement.GetProperty("structuredContent"); Assert.True(content.GetProperty("advisory").GetBoolean()); Assert.True(content.GetProperty("targetPlacement").TryGetProperty("existingFiles", out _));
    }

    [Fact]
    public async Task RecordsOnlyBoundedRevisionMetadataWhenAnActiveSessionIsSupplied()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:session", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Sessions.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        var sessions = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var started = await sessions.StartAsync(initialized.Value.StateLocation, new("session-preflight", "test", null, "agent", "test", "{}"), CancellationToken.None); Assert.True(started.IsSuccess);
        using var arguments = JsonDocument.Parse("""{"description":"create session with secret details","snippet":"const token = 'not-persisted';","targetStableIds":["node:session"],"sessionId":"session-preflight"}""");

        var result = await new PreflightChangeMcpTool().ExecuteAsync(new("preflight_change", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);

        Assert.True(result.IsSuccess); var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, "session-preflight", CancellationToken.None); Assert.True(events.IsSuccess);
        var recorded = Assert.Single(events.Value!, @event => @event.Kind == SessionEventKind.PreflightContextRecorded); Assert.Equal(1, recorded.GraphRevision);
        Assert.DoesNotContain("secret", recorded.PayloadJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("token", recorded.PayloadJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("snippet", recorded.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }
}
