using Archy.Features.Analysis.ExternalLanguageServerProtocol;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol;

public sealed class LanguageServerTranscriptVerifierTests
{
    [Fact]
    public void InitializationTranscriptRequiresResponseBeforeInitializedNotification()
    {
        IReadOnlyList<LspProtocolTranscriptAttempt> transcript = [Attempt(0, [Initialize(), InitializeResponse(), Initialized()])];

        Assert.Null(LanguageServerTranscriptVerifier.Validate(Specification(), transcript));
    }

    [Fact]
    public void InitializationTimeoutProducesARestartableTranscript()
    {
        IReadOnlyList<LspProtocolTranscriptAttempt> transcript =
        [
            Attempt(0, [Initialize(), Timeout("initialize")]),
            Attempt(1, [Restarted(), Initialize(), InitializeResponse(), Initialized()]),
        ];

        Assert.Null(LanguageServerTranscriptVerifier.Validate(Specification(), transcript));
        var decision = LanguageServerRestartPolicyEvaluator.Decide(Specification(), 0, LanguageServerFailureKind.InitializeTimeout);
        Assert.True(decision.ShouldRestart);
        Assert.Equal(1, decision.NextRestartNumber);
    }

    [Fact]
    public void MalformedResponseProducesARestartableTranscript()
    {
        IReadOnlyList<LspProtocolTranscriptAttempt> transcript =
        [
            Attempt(0, [Initialize(), new LspProtocolTranscriptEvent(LspProtocolTranscriptDirection.Server, LspProtocolTranscriptEventKind.MalformedResponse, null, null)]),
            Attempt(1, [Restarted(), Initialize(), InitializeResponse(), Initialized()]),
        ];

        Assert.Null(LanguageServerTranscriptVerifier.Validate(Specification(), transcript));
    }

    [Fact]
    public void RestartBudgetRejectsAnAdditionalCrashLoop()
    {
        IReadOnlyList<LspProtocolTranscriptAttempt> transcript =
        [
            Attempt(0, [Initialize(), Timeout("initialize")]),
            Attempt(1, [Restarted(), Initialize(), Timeout("initialize")]),
            Attempt(2, [Restarted(), Initialize(), Timeout("initialize")]),
        ];

        Assert.NotNull(LanguageServerTranscriptVerifier.Validate(Specification() with { RestartPolicy = new LanguageServerRestartPolicy(1, 10) }, transcript));
    }

    private static LspProtocolTranscriptAttempt Attempt(int number, IReadOnlyList<LspProtocolTranscriptEvent> events) => new(number, events);
    private static LspProtocolTranscriptEvent Restarted() => new(LspProtocolTranscriptDirection.Process, LspProtocolTranscriptEventKind.Restarted, null, null);
    private static LspProtocolTranscriptEvent Initialize() => new(LspProtocolTranscriptDirection.Host, LspProtocolTranscriptEventKind.JsonRpcRequest, 1, "initialize");
    private static LspProtocolTranscriptEvent InitializeResponse() => new(LspProtocolTranscriptDirection.Server, LspProtocolTranscriptEventKind.JsonRpcResponse, 1, null);
    private static LspProtocolTranscriptEvent Initialized() => new(LspProtocolTranscriptDirection.Host, LspProtocolTranscriptEventKind.JsonRpcNotification, null, "initialized");
    private static LspProtocolTranscriptEvent Timeout(string method) => new(LspProtocolTranscriptDirection.Host, LspProtocolTranscriptEventKind.RequestTimedOut, 1, method);

    private static LanguageServerLaunchSpecification Specification() => new(
        LanguageServerProtocolContract.CurrentSchemaVersion,
        "fixture-roslyn-lsp",
        "csharp",
        "fixture-lsp",
        ["--stdio"],
        "/repo",
        new LanguageServerTimeouts(5_000, 15_000, 5_000),
        new LanguageServerRestartPolicy(2, 10),
        1_048_576,
        65_536,
        [new LanguageServerCapabilityRequirement("definition", true)]);
}
