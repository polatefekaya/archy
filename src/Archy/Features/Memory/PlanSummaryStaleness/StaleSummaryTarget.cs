namespace Archy.Features.Memory.PlanSummaryStaleness;

public sealed record StaleSummaryTarget(
    string TargetStableId,
    IReadOnlyList<SummaryStalenessReason> Reasons);
