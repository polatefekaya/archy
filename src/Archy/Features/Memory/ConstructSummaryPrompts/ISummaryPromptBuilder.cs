namespace Archy.Features.Memory.ConstructSummaryPrompts;

public interface ISummaryPromptBuilder
{
    SummaryPrompt Build(SummaryPromptBuildRequest request);
}
