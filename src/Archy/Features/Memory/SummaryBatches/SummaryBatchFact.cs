namespace Archy.Features.Memory.SummaryBatches;

public sealed record SummaryBatchFact(
    string SummaryBatchId,
    string SessionId,
    string SettleReason,
    SummaryBatchRequestState RequestState,
    long SourceGraphRevision,
    string ModelRequestMetadataJson,
    IReadOnlyList<SummaryBatchMemberFact> Members);
