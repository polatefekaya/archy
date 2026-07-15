namespace Archy.Features.Memory.ConstructSummaryPrompts;

public sealed record SummaryPrompt(
    string TargetStableId,
    string Prompt,
    string OutputSchemaJson,
    int SourceCharacterCount,
    IReadOnlyList<string> IncludedSourcePaths);
