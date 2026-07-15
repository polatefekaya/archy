namespace Archy.Features.Analysis.InventorySources;

public sealed record SourcePathDecision(
    bool IsIncluded,
    SourcePathExclusionReason? ExclusionReason,
    string? MatchedRule)
{
    public static SourcePathDecision Include() => new(true, null, null);

    public static SourcePathDecision Exclude(SourcePathExclusionReason reason, string rule) =>
        new(false, reason, rule);
}
