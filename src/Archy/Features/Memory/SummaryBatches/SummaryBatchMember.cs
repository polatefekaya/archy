namespace Archy.Features.Memory.SummaryBatches;

public sealed record SummaryBatchMember(
    int MemberOrdinal,
    string TargetKind,
    string TargetStableId,
    int TouchOrdinal,
    IReadOnlyList<int> CoTouchedMemberOrdinals);
