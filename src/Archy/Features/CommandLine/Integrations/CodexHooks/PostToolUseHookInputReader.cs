using System.Text.Json;
using Archy.Features.Integrations.Codex.PostToolChangedPaths;

namespace Archy.Features.CommandLine.Integrations.CodexHooks;

internal static class PostToolUseHookInputReader
{
    public static async ValueTask<PostToolUseHookInput?> ReadAsync(CancellationToken cancellationToken)
    {
        var json = await Console.In.ReadToEndAsync(cancellationToken);
        if (json.Length > 128_000)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("cwd", out var cwd)
                || string.IsNullOrWhiteSpace(cwd.GetString())
                || !root.TryGetProperty("tool_name", out var toolName)
                || !TryMapToolKind(toolName.GetString(), out var toolKind))
            {
                return null;
            }

            var paths = root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object
                ? ReadKnownPaths(input)
                : [];
            return new PostToolUseHookInput(
                root.TryGetProperty("session_id", out var sessionId) ? sessionId.GetString() : null,
                cwd.GetString()!,
                toolKind,
                paths);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryMapToolKind(string? toolName, out PostToolKind toolKind)
    {
        toolKind = toolName switch
        {
            "Bash" => PostToolKind.Bash,
            "Edit" or "apply_patch" => PostToolKind.Edit,
            "Write" => PostToolKind.Write,
            _ => default,
        };
        return toolName is "Bash" or "Edit" or "apply_patch" or "Write";
    }

    private static string[] ReadKnownPaths(JsonElement input)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        AddString(input, "path", paths);
        AddString(input, "file_path", paths);
        AddString(input, "filePath", paths);
        if (input.TryGetProperty("paths", out var rawPaths) && rawPaths.ValueKind == JsonValueKind.Array)
        {
            foreach (var path in rawPaths.EnumerateArray())
            {
                if (path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString()))
                {
                    paths.Add(path.GetString()!);
                }
            }
        }

        return paths.Take(256).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    }

    private static void AddString(JsonElement input, string propertyName, HashSet<string> paths)
    {
        if (input.TryGetProperty(propertyName, out var path)
            && path.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(path.GetString()))
        {
            paths.Add(path.GetString()!);
        }
    }
}
