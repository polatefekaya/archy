namespace Archy.Features.Memory.QueueStaleSummaryRegeneration;

public interface IStaleSummaryRegenerationWorklist
{
    bool EnqueueIfStale(string targetStableId, SummaryRegenerationTrigger trigger);

    SummaryRegenerationRequest? Dequeue();

    void Complete(string targetStableId);

    void ReleaseForRetry(string targetStableId);
}
