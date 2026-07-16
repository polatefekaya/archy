using System.Text;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;

/// <summary>Replaces exactly one Archy-owned marker block and leaves every user-owned byte outside it untouched.</summary>
public sealed class ManagedAgentsSectionGenerator : IManagedAgentsSectionGenerator
{
    private const string BeginMarker = "<!-- archy:begin -->";
    private const string EndMarker = "<!-- archy:end -->";
    private const int MaximumManagedCharacters = 32_000;
    private const int MaximumItemsPerSection = 100;

    public Result<AgentsManagedSectionResult> Generate(string existingDocument, AgentsManagedSectionInput input)
    {
        ArgumentNullException.ThrowIfNull(existingDocument);
        ArgumentNullException.ThrowIfNull(input);
        var validation = Validate(input);
        if (validation is not null)
        {
            return ResultFactory.Failure<AgentsManagedSectionResult>(validation);
        }

        var managedContent = BuildContent(input);
        if (managedContent.Length > MaximumManagedCharacters)
        {
            return ResultFactory.Failure<AgentsManagedSectionResult>(Problem.Validation("The generated Archy AGENTS.md section exceeds its size limit."));
        }

        var begin = existingDocument.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = existingDocument.IndexOf(EndMarker, StringComparison.Ordinal);
        if (begin < 0 && end >= 0 || begin >= 0 && end < 0 || begin >= 0 && end < begin
            || HasAdditionalMarker(existingDocument, BeginMarker, begin)
            || HasAdditionalMarker(existingDocument, EndMarker, end))
        {
            return ResultFactory.Failure<AgentsManagedSectionResult>(Problem.Validation("AGENTS.md must contain at most one complete Archy managed marker pair."));
        }

        var document = begin < 0
            ? AppendSection(existingDocument, managedContent)
            : ReplaceSection(existingDocument, begin, end, managedContent);
        return ResultFactory.Success(new AgentsManagedSectionResult(document, !string.Equals(document, existingDocument, StringComparison.Ordinal), managedContent));
    }

    private static Problem? Validate(AgentsManagedSectionInput input)
    {
        if (input.GraphRevision < 1
            || input.LayerConventions is null
            || input.Exceptions is null
            || input.NextSessionNotes is null
            || input.LayerConventions.Count > MaximumItemsPerSection
            || input.Exceptions.Count > MaximumItemsPerSection
            || input.NextSessionNotes.Count > MaximumItemsPerSection
            || input.LayerConventions.Concat(input.Exceptions).Concat(input.NextSessionNotes).Any(static item =>
                string.IsNullOrWhiteSpace(item) || item.Contains("<!--", StringComparison.Ordinal) || item.Contains("-->", StringComparison.Ordinal)))
        {
            return Problem.Validation("Archy AGENTS.md generation requires bounded, marker-safe snapshot facts.");
        }

        return null;
    }

    private static bool HasAdditionalMarker(string document, string marker, int firstIndex) =>
        firstIndex >= 0 && document.IndexOf(marker, firstIndex + marker.Length, StringComparison.Ordinal) >= 0;

    private static string AppendSection(string existingDocument, string managedContent)
    {
        var separator = existingDocument.Length == 0 ? string.Empty : existingDocument.EndsWith('\n') ? "\n" : "\n\n";
        return $"{existingDocument}{separator}{BeginMarker}\n{managedContent}\n{EndMarker}\n";
    }

    private static string ReplaceSection(string existingDocument, int begin, int end, string managedContent)
    {
        var prefix = existingDocument[..begin];
        var suffix = existingDocument[(end + EndMarker.Length)..];
        return $"{prefix}{BeginMarker}\n{managedContent}\n{EndMarker}{suffix}";
    }

    private static string BuildContent(AgentsManagedSectionInput input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Archy architecture snapshot");
        builder.Append("Graph revision: ").Append(input.GraphRevision).AppendLine();
        AppendSection(builder, "Resolved layer conventions", input.LayerConventions);
        AppendSection(builder, "Active exceptions", input.Exceptions);
        AppendSection(builder, "Next-session notes", input.NextSessionNotes);
        builder.AppendLine("This is generated context, not a write interceptor. Start a new Codex task after it changes, and run `archy verify` before delivery.");
        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendSection(StringBuilder builder, string heading, IReadOnlyList<string> items)
    {
        builder.AppendLine();
        builder.AppendLine($"### {heading}");
        if (items.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        foreach (var item in items.OrderBy(static item => item, StringComparer.Ordinal))
        {
            builder.Append("- ").AppendLine(item.Trim());
        }
    }
}
