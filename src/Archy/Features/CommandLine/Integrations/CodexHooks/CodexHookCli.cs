using Mediator;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

/// <summary>Private command surface invoked only by the bundled Codex lifecycle hooks.</summary>
public static class CodexHookCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, IServiceProvider? serviceProvider, CancellationToken cancellationToken)
    {
        if (args is ["post-tool-use"])
        {
            var postToolUseInput = await PostToolUseHookInputReader.ReadAsync(cancellationToken);
            return postToolUseInput is null
                ? 0
                : await new PostToolUseCodexHook(
                    mediator,
                    serviceProvider?.GetService(typeof(Archy.Features.Integrations.Codex.RecordHookEvents.ICodexHookEventRecorder)) as Archy.Features.Integrations.Codex.RecordHookEvents.ICodexHookEventRecorder
                    ?? new Archy.Features.Integrations.Codex.RecordHookEvents.NoOpCodexHookEventRecorder()).RunAsync(postToolUseInput, cancellationToken);
        }

        if (args is ["stop"])
        {
            var stopInput = await CodexHookInputReader.ReadAsync(cancellationToken);
            return stopInput is null ? 0 : await new StopCodexHook(mediator).RunAsync(stopInput, cancellationToken);
        }

        if (args is not ["session-start"])
        {
            return 64;
        }

        var input = await CodexHookInputReader.ReadAsync(cancellationToken);
        if (input is null)
        {
            return 0;
        }

        return await new SessionStartCodexHook(mediator).RunAsync(input, cancellationToken);
    }
}
