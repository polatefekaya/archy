namespace Archy.Features.Memory.SummaryBatches;

public sealed record SummaryBatch(
    string SummaryBatchId,
    string SessionId,
    string SettleReason,
    SummaryBatchRequestState RequestState,
    long SourceGraphRevision,
    string ModelRequestMetadataJson,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SummaryBatchMember> Members);
