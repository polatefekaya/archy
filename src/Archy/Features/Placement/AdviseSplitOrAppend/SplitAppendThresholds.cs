namespace Archy.Features.Placement.AdviseSplitOrAppend;

/// <summary>Repository-configurable advisory thresholds; none of these values are enforcement rules.</summary>
public sealed record SplitAppendThresholds(int LargeModuleMemberCount, double LowCohesionThreshold, double HighFanOutThreshold)
{
    public static SplitAppendThresholds Default { get; } = new(24, .45d, 12d);
}
