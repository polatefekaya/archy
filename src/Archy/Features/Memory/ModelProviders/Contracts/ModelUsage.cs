namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record ModelUsage(
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    int TotalTokens);
