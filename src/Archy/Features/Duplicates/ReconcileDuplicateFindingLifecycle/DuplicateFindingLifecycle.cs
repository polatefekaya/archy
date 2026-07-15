namespace Archy.Features.Duplicates.ReconcileDuplicateFindingLifecycle;

public sealed record DuplicateFindingLifecycle(string FindingId, long GraphRevision, DuplicateFindingLifecycleState State, string? SupersededByFindingId, string Reason, DateTimeOffset OccurredAtUtc);
