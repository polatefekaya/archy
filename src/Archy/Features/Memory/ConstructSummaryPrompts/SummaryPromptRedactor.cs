using System.Text.RegularExpressions;

namespace Archy.Features.Memory.ConstructSummaryPrompts;

/// <summary>Conservative outbound redaction. It intentionally runs again even when source was previously inspected locally.</summary>
public sealed partial class SummaryPromptRedactor : ISummaryPromptRedactor
{
    public string Redact(string untrustedRepositoryText)
    {
        ArgumentNullException.ThrowIfNull(untrustedRepositoryText);
        var withoutOpenAiKeys = OpenAiKey().Replace(untrustedRepositoryText, "[REDACTED_OPENAI_KEY]");
        return SensitiveAssignment().Replace(withoutOpenAiKeys, "${name}=[REDACTED_SECRET]");
    }

    [GeneratedRegex(@"\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiKey();

    [GeneratedRegex("""(?im)\b(?<name>api[_-]?key|secret|password|token)\b\s*(?:=|:)\s*(?:\"[^\"]*\"|'[^']*'|[^\s,;]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignment();
}
