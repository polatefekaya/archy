namespace Archy.Features.CommandLine.Integrations.CodexHooks;

internal sealed record CodexHookInput(string SessionId, string WorkingDirectory, string? Model);
