using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

public static class LanguageServerTranscriptVerifier
{
    public static Problem? Validate(
        LanguageServerLaunchSpecification specification,
        IReadOnlyList<LspProtocolTranscriptAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(attempts);

        if (attempts.Count == 0 || attempts.Count > specification.RestartPolicy.MaximumRestarts + 1)
        {
            return Problem.Validation("Language-server transcripts must contain one initial attempt and cannot exceed the configured restart budget.");
        }

        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index];
            if (attempt is null || attempt.AttemptNumber != index || attempt.Events is null || attempt.Events.Count == 0)
            {
                return Problem.Validation("Language-server transcript attempts must be ordered and contain events.");
            }

            var validation = ValidateAttempt(attempt, index > 0);
            if (validation is not null)
            {
                return validation;
            }

            var terminalFailure = TerminalFailure(attempt.Events);
            if (terminalFailure is null && index != attempts.Count - 1)
            {
                return Problem.Validation("Only a failed language-server attempt may be followed by a restart.");
            }

            if (terminalFailure is not null && index < attempts.Count - 1)
            {
                var decision = LanguageServerRestartPolicyEvaluator.Decide(specification, index, terminalFailure.Value);
                if (!decision.ShouldRestart)
                {
                    return Problem.Validation("Language-server transcript restarts exceed the configured restart budget.");
                }
            }
        }

        return null;
    }

    private static Problem? ValidateAttempt(LspProtocolTranscriptAttempt attempt, bool isRestart)
    {
        var events = attempt.Events;
        var initializationOffset = isRestart ? 1 : 0;
        if (isRestart && !Is(events[0], LspProtocolTranscriptDirection.Process, LspProtocolTranscriptEventKind.Restarted, null, null))
        {
            return Problem.Validation("Every restarted language-server attempt must begin with a process restart event.");
        }

        if (events.Count <= initializationOffset || !Is(events[initializationOffset], LspProtocolTranscriptDirection.Host, LspProtocolTranscriptEventKind.JsonRpcRequest, LanguageServerProtocolContract.InitializeRequestId, "initialize"))
        {
            return Problem.Validation("Every language-server attempt must begin by sending initialize request ID 1.");
        }

        var failure = TerminalFailure(events);
        if (failure is not null)
        {
            return ValidFailure(events[^1], failure.Value)
                ? null
                : Problem.Validation("Language-server attempt failure events must be terminal and classified.");
        }

        if (events.Count != initializationOffset + 3 ||
            !Is(events[initializationOffset + 1], LspProtocolTranscriptDirection.Server, LspProtocolTranscriptEventKind.JsonRpcResponse, LanguageServerProtocolContract.InitializeRequestId, null) ||
            !Is(events[initializationOffset + 2], LspProtocolTranscriptDirection.Host, LspProtocolTranscriptEventKind.JsonRpcNotification, null, "initialized"))
        {
            return Problem.Validation("A successful language-server initialization must receive initialize response ID 1 before sending initialized notification.");
        }

        return null;
    }

    private static LanguageServerFailureKind? TerminalFailure(IReadOnlyList<LspProtocolTranscriptEvent> events) =>
        events[^1].Kind switch
        {
            LspProtocolTranscriptEventKind.RequestTimedOut when string.Equals(events[^1].Method, "initialize", StringComparison.Ordinal) => LanguageServerFailureKind.InitializeTimeout,
            LspProtocolTranscriptEventKind.RequestTimedOut => LanguageServerFailureKind.RequestTimeout,
            LspProtocolTranscriptEventKind.MalformedResponse => LanguageServerFailureKind.MalformedResponse,
            LspProtocolTranscriptEventKind.ProcessExited => LanguageServerFailureKind.ProcessExited,
            _ => null,
        };

    private static bool ValidFailure(LspProtocolTranscriptEvent terminal, LanguageServerFailureKind failure) => failure switch
    {
        LanguageServerFailureKind.InitializeTimeout or LanguageServerFailureKind.RequestTimeout => terminal.Direction == LspProtocolTranscriptDirection.Host,
        LanguageServerFailureKind.MalformedResponse => terminal.Direction == LspProtocolTranscriptDirection.Server,
        LanguageServerFailureKind.ProcessExited => terminal.Direction == LspProtocolTranscriptDirection.Process,
        _ => false,
    };

    private static bool Is(
        LspProtocolTranscriptEvent value,
        LspProtocolTranscriptDirection direction,
        LspProtocolTranscriptEventKind kind,
        int? requestId,
        string? method) =>
        value.Direction == direction &&
        value.Kind == kind &&
        value.RequestId == requestId &&
        string.Equals(value.Method, method, StringComparison.Ordinal);
}
