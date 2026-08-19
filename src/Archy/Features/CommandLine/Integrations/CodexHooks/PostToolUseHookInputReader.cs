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

    /// <summary>Maps a host tool name onto the kind of change that tool performs.</summary>
    /// <remarks>
    /// Names arrive from more than one agent host: <c>apply_patch</c> is Codex, while
    /// <c>MultiEdit</c> and <c>NotebookEdit</c> are Claude Code. An unmapped editing tool is not a
    /// harmless omission — the write still lands and the architecture check is silently skipped
    /// for it, which reads as "no findings". Keep every file-mutating tool of every supported host
    /// in this switch.
    /// </remarks>
    private static bool TryMapToolKind(string? toolName, out PostToolKind toolKind)
    {
        switch (toolName)
        {
            case "Bash":
                toolKind = PostToolKind.Bash;
                return true;
            case "Edit" or "MultiEdit" or "NotebookEdit" or "apply_patch":
                toolKind = PostToolKind.Edit;
                return true;
            case "Write":
                toolKind = PostToolKind.Write;
                return true;
            default:
                toolKind = default;
                return false;
        }
    }

    private static string[] ReadKnownPaths(JsonElement input)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        AddString(input, "path", paths);
        AddString(input, "file_path", paths);
        AddString(input, "filePath", paths);

        // NotebookEdit reports its target under a distinct property name.
        AddString(input, "notebook_path", paths);
        AddString(input, "notebookPath", paths);
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
