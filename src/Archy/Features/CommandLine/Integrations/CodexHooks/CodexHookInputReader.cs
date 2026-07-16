using System.Text.Json;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

internal static class CodexHookInputReader
{
    public static async ValueTask<CodexHookInput?> ReadAsync(CancellationToken cancellationToken)
    {
        var json = await Console.In.ReadToEndAsync(cancellationToken);
        if (json.Length > 128_000)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var input = document.RootElement;
            if (input.ValueKind != JsonValueKind.Object
                || !input.TryGetProperty("session_id", out var sessionId)
                || string.IsNullOrWhiteSpace(sessionId.GetString())
                || !input.TryGetProperty("cwd", out var cwd)
                || string.IsNullOrWhiteSpace(cwd.GetString()))
            {
                return null;
            }

            return new CodexHookInput(
                sessionId.GetString()!,
                cwd.GetString()!,
                input.TryGetProperty("model", out var model) ? model.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
