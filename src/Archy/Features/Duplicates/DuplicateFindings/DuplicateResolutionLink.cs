namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed record DuplicateResolutionLink(
    string FindingId,
    string DecisionId,
    DateTimeOffset LinkedAtUtc);
