using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Sessions.ArchitectureSessions;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.SharedKernel.Primitives;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Sessions;

public sealed class ArchitectureSessionRepositoryTests
{
    [Fact]
    public async Task StartRejectsDuplicateSessionAndExternalIdentitiesWithoutAppendingEvents()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sessions = CreateSessionStore();
        var start = CreateSessionStart("session-idempotency");

        var blankExternalIdentity = await sessions.StartAsync(
            initialized.Value.StateLocation,
            start with { SessionId = "session-blank-external", ExternalSessionId = " " },
            CancellationToken.None);
        Assert.False(blankExternalIdentity.IsSuccess);
        Assert.Equal("validation", blankExternalIdentity.Problem!.Code);

        var first = await sessions.StartAsync(initialized.Value.StateLocation, start, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var duplicateSession = await sessions.StartAsync(
            initialized.Value.StateLocation,
            start with { ExternalSessionId = "external:another" },
            CancellationToken.None);
        Assert.False(duplicateSession.IsSuccess);
        Assert.Equal("conflict", duplicateSession.Problem!.Code);

        var duplicateExternal = await sessions.StartAsync(
            initialized.Value.StateLocation,
            start with { SessionId = "session-different" },
            CancellationToken.None);
        Assert.False(duplicateExternal.IsSuccess);
        Assert.Equal("conflict", duplicateExternal.Problem!.Code);

        var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, start.SessionId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        Assert.Single(events.Value);
        Assert.Equal(SessionEventKind.SessionStarted, events.Value[0].Kind);
    }

    [Fact]
    public async Task ListingEventsForAnUnknownSessionReturnsNotFoundInsteadOfAnAmbiguousEmptyHistory()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var result = await CreateSessionStore().ListEventsAsync(
            initialized.Value.StateLocation,
            "session-missing",
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Problem!.Code);
    }

    [Fact]
    public async Task SessionEventsAcceptExistingGraphEdgeAndSymbolTargets()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var node = GraphRevisionTestBuilder.Node("method:archy.program.run", "hash:method:v1");
        var edge = GraphRevisionTestBuilder.Edge("calls:archy.program.run:dependency", node.StableId, node.StableId);
        var symbol = new GraphSymbolFact(
            "symbol:archy.program.run",
            node.StableId,
            "Archy.Program.Run()",
            "public",
            "Run()",
            "[]",
            "{\"type\":\"void\"}",
            "hash:symbol:v1");
        var revision = await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [node],
            [edge],
            [symbol]);
        var sessions = CreateSessionStore();
        var started = await sessions.StartAsync(
            initialized.Value.StateLocation,
            CreateSessionStart("session-graph-targets"),
            CancellationToken.None);
        Assert.True(started.IsSuccess);

        var edgeEvent = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact(
                SessionEventKind.FileTouched,
                revision,
                new ArchitectureTarget(ArchitectureTargetKind.GraphEdge, edge.EdgeId),
                "{}"),
            CancellationToken.None);
        var symbolEvent = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact(
                SessionEventKind.ValidationCompleted,
                revision,
                new ArchitectureTarget(ArchitectureTargetKind.GraphSymbol, symbol.SymbolId),
                "{}"),
            CancellationToken.None);

        Assert.True(edgeEvent.IsSuccess);
        Assert.True(symbolEvent.IsSuccess);
        Assert.Equal(2, edgeEvent.Value.SequenceNumber);
        Assert.Equal(3, symbolEvent.Value.SequenceNumber);
    }

    [Fact]
    public async Task AppendingRejectsUndefinedEventAndTargetKindsWithoutMutatingTheSession()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sessions = CreateSessionStore();
        var started = await sessions.StartAsync(
            initialized.Value.StateLocation,
            CreateSessionStart("session-invalid-enums"),
            CancellationToken.None);
        Assert.True(started.IsSuccess);

        var undefinedEvent = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact((SessionEventKind)999, null, null, "{}"),
            CancellationToken.None);
        var undefinedTarget = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact(
                SessionEventKind.FileTouched,
                null,
                new ArchitectureTarget((ArchitectureTargetKind)999, "unknown:target"),
                "{}"),
            CancellationToken.None);

        Assert.False(undefinedEvent.IsSuccess);
        Assert.Equal("validation", undefinedEvent.Problem!.Code);
        Assert.False(undefinedTarget.IsSuccess);
        Assert.Equal("validation", undefinedTarget.Problem!.Code);
        var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, started.Value.SessionId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        Assert.Single(events.Value);
    }

    [Fact]
    public async Task SessionLifecycleIsOrderedAppendOnlyAndRejectsEventsAfterEnd()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitSourceRevisionAsync(initialized.Value.StateLocation);
        var sessions = CreateSessionStore();

        var started = await sessions.StartAsync(
            initialized.Value.StateLocation,
            CreateSessionStart("session-lifecycle"),
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        Assert.False(started.Value.IsEnded);

        var touched = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact(
                SessionEventKind.FileTouched,
                revision,
                new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "type:archy.program"),
                "{\"path\":\"Program.cs\"}"),
            CancellationToken.None);
        Assert.True(touched.IsSuccess);
        Assert.Equal(2, touched.Value.SequenceNumber);

        var ended = await sessions.EndAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            "{\"reason\":\"stop_hook\"}",
            CancellationToken.None);
        Assert.True(ended.IsSuccess);
        Assert.Equal(3, ended.Value.SequenceNumber);

        var session = await sessions.GetAsync(initialized.Value.StateLocation, started.Value.SessionId, CancellationToken.None);
        Assert.True(session.IsSuccess);
        Assert.True(session.Value.IsEnded);
        Assert.Equal(ended.Value.OccurredAtUtc, session.Value.EndedAtUtc);

        var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, started.Value.SessionId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        Assert.Collection(
            events.Value,
            sessionEvent => Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind),
            sessionEvent =>
            {
                Assert.Equal(SessionEventKind.FileTouched, sessionEvent.Kind);
                Assert.Equal(revision, sessionEvent.GraphRevision);
                Assert.Equal("type:archy.program", sessionEvent.Target!.StableId);
            },
            sessionEvent => Assert.Equal(SessionEventKind.SessionEnded, sessionEvent.Kind));

        var rejected = await sessions.AppendEventAsync(
            initialized.Value.StateLocation,
            started.Value.SessionId,
            new SessionEventFact(SessionEventKind.SummaryBatchRequested, revision, null, "{}"),
            CancellationToken.None);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);
    }

    [Fact]
    public async Task RecordingADecisionPersistsTargetsAndAnAtomicReplayEvent()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await CommitSourceRevisionAsync(initialized.Value.StateLocation);
        var sessions = CreateSessionStore();
        var started = await sessions.StartAsync(
            initialized.Value.StateLocation,
            CreateSessionStart("session-decision"),
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var target = new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "type:archy.program");

        var recorded = await decisions.RecordAsync(
            initialized.Value.StateLocation,
            new ArchitectureDecisionFact(
                "duplicate_review",
                DecisionResolution.Modified,
                "Kept the existing abstraction and extended its contract.",
                "codex_agent",
                "thread:fixture",
                started.Value.SessionId,
                revision,
                [target, new ArchitectureTarget(ArchitectureTargetKind.Rule, "rules:application-to-domain")]),
            CancellationToken.None);
        Assert.True(recorded.IsSuccess);
        Assert.Equal(DecisionResolution.Modified, recorded.Value.Resolution);
        Assert.Equal(2, recorded.Value.Targets.Count);

        var related = await decisions.ListForTargetAsync(initialized.Value.StateLocation, target, CancellationToken.None);
        Assert.True(related.IsSuccess);
        var decision = Assert.Single(related.Value);
        Assert.Equal(recorded.Value.DecisionId, decision.DecisionId);
        Assert.Equal("duplicate_review", decision.DecisionType);
        Assert.Equal(started.Value.SessionId, decision.SessionId);
        Assert.Equal(revision, decision.GraphRevision);
        Assert.Equal(recorded.Value.Targets, decision.Targets);

        var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, started.Value.SessionId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        Assert.Collection(
            events.Value,
            sessionEvent => Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind),
            sessionEvent =>
            {
                Assert.Equal(SessionEventKind.DecisionRecorded, sessionEvent.Kind);
                Assert.Equal(recorded.Value.DecisionId, sessionEvent.DecisionId);
                Assert.Equal(revision, sessionEvent.GraphRevision);
            });
    }

    [Fact]
    public async Task RecordingAnInvalidGraphTargetLeavesNoDecisionOrReplayEvent()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sessions = CreateSessionStore();
        var started = await sessions.StartAsync(
            initialized.Value.StateLocation,
            CreateSessionStart("session-invalid-target"),
            CancellationToken.None);
        Assert.True(started.IsSuccess);
        var decisions = new ArchitectureDecisionRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var rejected = await decisions.RecordAsync(
            initialized.Value.StateLocation,
            new ArchitectureDecisionFact(
                "duplicate_review",
                DecisionResolution.Ignored,
                null,
                "codex_agent",
                "thread:fixture",
                started.Value.SessionId,
                null,
                [new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "type:missing")]),
            CancellationToken.None);
        Assert.False(rejected.IsSuccess);
        Assert.Equal("conflict", rejected.Problem!.Code);

        var related = await decisions.ListForTargetAsync(
            initialized.Value.StateLocation,
            new ArchitectureTarget(ArchitectureTargetKind.GraphNode, "type:missing"),
            CancellationToken.None);
        Assert.True(related.IsSuccess);
        Assert.Empty(related.Value);

        var events = await sessions.ListEventsAsync(initialized.Value.StateLocation, started.Value.SessionId, CancellationToken.None);
        Assert.True(events.IsSuccess);
        var sessionEvent = Assert.Single(events.Value);
        Assert.Equal(SessionEventKind.SessionStarted, sessionEvent.Kind);
    }

    private static ArchitectureSessionRepository CreateSessionStore() =>
        new(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

    private static SessionStartFact CreateSessionStart(string sessionId) => new(
        sessionId,
        "codex",
        $"external:{sessionId}",
        "codex_agent",
        "thread:fixture",
        "{\"repository\":\"fixture\"}");

    private static async Task<long> CommitSourceRevisionAsync(
        Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location)
    {
        var run = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: "deadbeef",
            CancellationToken.None);
        Assert.True(run.IsSuccess);

        var committed = await new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).CommitAsync(
            location,
            run.Value.RunId,
            [new GraphNodeFact("type:archy.program", "type", "Archy.Program", "Program", "Program.cs", 1, 10, "fixture", 1, "{}", "hash:node:v1")],
            [],
            CancellationToken.None);
        Assert.True(committed.IsSuccess);
        return committed.Value.Revision;
    }
}
