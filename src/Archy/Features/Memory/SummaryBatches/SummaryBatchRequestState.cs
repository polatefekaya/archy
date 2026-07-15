namespace Archy.Features.Memory.SummaryBatches;

public enum SummaryBatchRequestState
{
    Pending,
    Requested,
    Completed,
    Degraded,
    Skipped,
}
