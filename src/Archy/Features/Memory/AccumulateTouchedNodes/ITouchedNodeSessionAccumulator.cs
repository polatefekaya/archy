namespace Archy.Features.Memory.AccumulateTouchedNodes;

public interface ITouchedNodeSessionAccumulator
{
    int Record(TouchedNodeRecord record);

    TouchedNodeBatchCandidate? PrepareFlush(
        string sessionId,
        string summaryBatchId,
        string settleReason,
        string modelRequestMetadataJson,
        bool isSessionEnd = false);

    /// <summary>Discard only the members durably written by <see cref="PrepareFlush"/>.</summary>
    void AcknowledgePersisted(string sessionId, string summaryBatchId);
}
