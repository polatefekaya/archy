namespace Archy.Features.Memory.DetermineImportantNodes;

public sealed record ImportantNodeEligibility(
    long SourceGraphRevision,
    IReadOnlyList<ImportantNodeTarget> Included,
    IReadOnlyList<ImportantNodeExclusion> Excluded);

/// <summary>Records an exclusion in a machine-readable form rather than silently losing a candidate.</summary>
public sealed record ImportantNodeExclusion(
    string StableId,
    string DisplayName,
    string Reason);
