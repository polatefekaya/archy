namespace Archy.Features.Memory.PlanSummaryStaleness;

public sealed record SummaryStalenessPlan(
    long SourceGraphRevision,
    IReadOnlyList<StaleSummaryTarget> Targets)
{
    public bool HasChanges => Targets.Count > 0;
}
