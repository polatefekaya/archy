using Archy.Features.Integrations.Codex.PostToolChangedPaths;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

internal sealed record PostToolUseHookInput(
    string? SessionId,
    string WorkingDirectory,
    PostToolKind ToolKind,
    IReadOnlyList<string> ReportedPaths);
