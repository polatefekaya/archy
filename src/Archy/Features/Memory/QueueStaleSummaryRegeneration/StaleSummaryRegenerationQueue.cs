namespace Archy.Features.Memory.QueueStaleSummaryRegeneration;

/// <summary>Coalesces view/edit demand. A caller supplies stale targets; this queue never treats an arbitrary graph node as stale.</summary>
public sealed class StaleSummaryRegenerationWorklist(TimeProvider timeProvider) : IStaleSummaryRegenerationWorklist
{
    private readonly object gate = new();
    private readonly Queue<SummaryRegenerationRequest> queued = new();
    private readonly Dictionary<string, SummaryRegenerationRequest> outstanding = new(StringComparer.Ordinal);
    private readonly HashSet<string> inFlight = new(StringComparer.Ordinal);

    public bool EnqueueIfStale(string targetStableId, SummaryRegenerationTrigger trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetStableId);
        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        lock (gate)
        {
            if (!outstanding.TryAdd(targetStableId, new SummaryRegenerationRequest(targetStableId, trigger, timeProvider.GetUtcNow())))
            {
                return false;
            }

            queued.Enqueue(outstanding[targetStableId]);
            return true;
        }
    }

    public SummaryRegenerationRequest? Dequeue()
    {
        lock (gate)
        {
            while (queued.Count > 0)
            {
                var candidate = queued.Dequeue();
                if (inFlight.Add(candidate.TargetStableId))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    public void Complete(string targetStableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetStableId);
        lock (gate)
        {
            inFlight.Remove(targetStableId);
            outstanding.Remove(targetStableId);
        }
    }

    public void ReleaseForRetry(string targetStableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetStableId);
        lock (gate)
        {
            if (!inFlight.Remove(targetStableId) || !outstanding.TryGetValue(targetStableId, out var request))
            {
                return;
            }

            queued.Enqueue(request);
        }
    }
}
