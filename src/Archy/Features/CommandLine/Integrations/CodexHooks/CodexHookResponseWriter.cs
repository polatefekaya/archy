using System.Text.Json;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

internal static class CodexHookResponseWriter
{
    public static Task SessionStartContextAsync(string context) =>
        Console.Out.WriteLineAsync($"{{\"continue\":true,\"hookSpecificOutput\":{{\"hookEventName\":\"SessionStart\",\"additionalContext\":{String(context)}}}}}");

    public static Task PostToolUseAsync(bool continueTurn, string message) =>
        Console.Out.WriteLineAsync($"{{\"continue\":{(continueTurn ? "true" : "false")},\"stopReason\":{String(message)},\"systemMessage\":{String(message)}}}");

    public static Task StopAsync(string message) =>
        Console.Out.WriteLineAsync($"{{\"continue\":true,\"systemMessage\":{String(message)}}}");

    public static string String(string value) => $"\"{JsonEncodedText.Encode(value).ToString()}\"";
}
