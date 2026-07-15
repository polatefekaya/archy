namespace Archy.Features.Memory.ConstructSummaryPrompts;

/// <summary>Already-selected source content. Callers must not pass arbitrary repository files to prompt construction.</summary>
public sealed record SummaryPromptSource(string RepositoryRelativePath, string Content);
