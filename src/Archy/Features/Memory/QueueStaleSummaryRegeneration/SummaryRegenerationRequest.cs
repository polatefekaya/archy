namespace Archy.Features.Memory.QueueStaleSummaryRegeneration;

public sealed record SummaryRegenerationRequest(
    string TargetStableId,
    SummaryRegenerationTrigger Trigger,
    DateTimeOffset RequestedAtUtc);
