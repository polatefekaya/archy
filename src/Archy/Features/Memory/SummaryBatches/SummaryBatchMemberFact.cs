namespace Archy.Features.Memory.SummaryBatches;

/// <summary>The member ordinal is the deterministic position in <see cref="SummaryBatchFact.Members"/>.</summary>
public sealed record SummaryBatchMemberFact(
    string TargetKind,
    string TargetStableId,
    int TouchOrdinal,
    IReadOnlyList<int> CoTouchedMemberOrdinals);
