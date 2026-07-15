namespace Archy.Features.Memory.ConstructSummaryPrompts;

public interface ISummaryPromptRedactor
{
    string Redact(string untrustedRepositoryText);
}
