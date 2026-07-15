using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ParseJscpdCloneReport;

/// <summary>Consumes only the normalized worker result, never a jscpd report file or arbitrary sidecar stdout.</summary>
public sealed class JscpdCloneReportParser : IJscpdCloneReportParser
{
    public Result<JscpdCloneReport> Parse(string resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return ResultFactory.Failure<JscpdCloneReport>(Problem.Validation("jscpd returned no structured clone result."));
        }

        try
        {
            using var document = JsonDocument.Parse(resultJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || String(root, "toolVersion") is not { } toolVersion ||
                !root.TryGetProperty("clones", out var clones) || clones.ValueKind != JsonValueKind.Array)
            {
                return Invalid();
            }

            var parsed = new List<StructuralCloneOccurrence>();
            foreach (var clone in clones.EnumerateArray())
            {
                if (clone.ValueKind != JsonValueKind.Object || !clone.TryGetProperty("first", out var first) || !clone.TryGetProperty("second", out var second) ||
                    !clone.TryGetProperty("tokens", out var tokens) || !tokens.TryGetInt32(out var tokenCount) ||
                    !clone.TryGetProperty("lines", out var lines) || !lines.TryGetInt32(out var lineCount) || String(clone, "format") is not { } format)
                {
                    return Invalid();
                }

                var firstRange = Range(first);
                var secondRange = Range(second);
                if (firstRange is null || secondRange is null || tokenCount < 1 || lineCount < 1 || string.IsNullOrWhiteSpace(format))
                {
                    return Invalid();
                }

                parsed.Add(new StructuralCloneOccurrence(firstRange, secondRange, tokenCount, lineCount, format));
            }

            return ResultFactory.Success(new JscpdCloneReport(toolVersion, [.. parsed.OrderBy(CloneKey, StringComparer.Ordinal)]));
        }
        catch (JsonException)
        {
            return ResultFactory.Failure<JscpdCloneReport>(Problem.Validation("jscpd returned malformed clone JSON."));
        }
    }

    private static CloneSourceRange? Range(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || String(element, "path") is not { } path ||
            !element.TryGetProperty("startLine", out var startLine) || !startLine.TryGetInt32(out var startLineValue) ||
            !element.TryGetProperty("startColumn", out var startColumn) || !startColumn.TryGetInt32(out var startColumnValue) ||
            !element.TryGetProperty("endLine", out var endLine) || !endLine.TryGetInt32(out var endLineValue) ||
            !element.TryGetProperty("endColumn", out var endColumn) || !endColumn.TryGetInt32(out var endColumnValue) ||
            !IsRepositoryRelative(path) || startLineValue < 1 || startColumnValue < 1 || endLineValue < startLineValue || endColumnValue < 1)
        {
            return null;
        }

        return new CloneSourceRange(path, startLineValue, startColumnValue, endLineValue, endColumnValue);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool IsRepositoryRelative(string path) => !Path.IsPathRooted(path) && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or "..");
    private static string CloneKey(StructuralCloneOccurrence clone) => $"{clone.First.RepositoryRelativePath}\u001f{clone.First.StartLine:D8}\u001f{clone.Second.RepositoryRelativePath}\u001f{clone.Second.StartLine:D8}";
    private static Result<JscpdCloneReport> Invalid() => ResultFactory.Failure<JscpdCloneReport>(Problem.Validation("jscpd returned an invalid clone occurrence."));
}
