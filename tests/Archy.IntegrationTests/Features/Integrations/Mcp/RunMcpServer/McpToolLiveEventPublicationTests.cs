using System.Text.Json;
using Archy.Features.Integrations.Codex.RecordHookEvents;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpToolLiveEventPublicationTests
{
    [Fact]
    public async Task StartSessionPublishesTheCommittedSessionStartedEvent()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var publisher = new HookEventPublisher();
        using var subscription = publisher.Subscribe();
        var tool = new StartSessionMcpTool(publisher);

        var result = await tool.ExecuteAsync(Invocation(
            "start_session",
            """{"sessionId":"mcp-live-start","actorKind":"agent","actorId":"fixture"}""",
            fixture.Repository.Root,
            initialized.Value.StateLocation), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var published = await subscription.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal(SessionEventKind.SessionStarted, published.SessionEvent.Kind);
        Assert.Equal("mcp-live-start", published.SessionEvent.SessionId);
        Assert.Equal(1, published.SessionEvent.SequenceNumber);
    }

    [Fact]
    public async Task RecordedDecisionPublishesItsCommittedDecisionEventWhenAttributedToASession()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sessions = new ArchitectureSessionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var started = await sessions.StartAsync(initialized.Value.StateLocation, new SessionStartFact("mcp-live-decision", "mcp", null, "agent", "fixture", "{}"), CancellationToken.None);
        Assert.True(started.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("node:fixture", "fixture-hash");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        var publisher = new HookEventPublisher();
        using var subscription = publisher.Subscribe();
        var tool = new RecordDecisionMcpTool(publisher);

        var result = await tool.ExecuteAsync(Invocation(
            "record_decision",
            $$"""{"decisionType":"duplicate_review","actorKind":"agent","actorId":"fixture","resolution":"accepted","sessionId":"mcp-live-decision","graphRevision":{{revision}},"targets":[{"kind":"graph_node","stableId":"node:fixture"}]}""",
            fixture.Repository.Root,
            initialized.Value.StateLocation), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var published = await subscription.Reader.ReadAsync(CancellationToken.None);
        Assert.Equal(SessionEventKind.DecisionRecorded, published.SessionEvent.Kind);
        Assert.Equal("mcp-live-decision", published.SessionEvent.SessionId);
        Assert.NotNull(published.SessionEvent.DecisionId);
        Assert.Equal(2, published.SessionEvent.SequenceNumber);
    }

    private static McpToolInvocation Invocation(string name, string arguments, string root, Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location)
    {
        using var document = JsonDocument.Parse(arguments);
        return new McpToolInvocation(name, document.RootElement.Clone(), new McpWorkspaceContext(root, location));
    }
}
