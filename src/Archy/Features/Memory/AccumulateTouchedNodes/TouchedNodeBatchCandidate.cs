using Archy.Features.Memory.SummaryBatches;

namespace Archy.Features.Memory.AccumulateTouchedNodes;

/// <summary>A durable batch fact waiting for the caller's transaction boundary and repository persistence.</summary>
public sealed record TouchedNodeBatchCandidate(
    SummaryBatchFact Batch,
    DateTimeOffset FirstTouchedAtUtc,
    DateTimeOffset LastTouchedAtUtc);
