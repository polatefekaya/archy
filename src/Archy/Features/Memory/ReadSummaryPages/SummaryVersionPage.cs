using Archy.Features.Memory.Summaries;

namespace Archy.Features.Memory.ReadSummaryPages;

/// <summary>A bounded chronological page of immutable versions for one summary identity.</summary>
public sealed record SummaryVersionPage(
    string SummaryId,
    int Offset,
    int Limit,
    int TotalCount,
    IReadOnlyList<SummaryVersion> Items);
