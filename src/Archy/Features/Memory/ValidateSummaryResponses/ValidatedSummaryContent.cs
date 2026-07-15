namespace Archy.Features.Memory.ValidateSummaryResponses;

public sealed record ValidatedSummaryContent(
    string TargetStableId,
    string Summary,
    string EnglishDiff);
