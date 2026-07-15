namespace Archy.Features.Memory.DetermineImportantNodes;

/// <summary>A deterministic summary target and the source paths that make it relevant to a change.</summary>
public sealed record ImportantNodeTarget(
    ImportantNodeTargetKind Kind,
    string StableId,
    string DisplayName,
    IReadOnlyList<string> SourcePaths,
    IReadOnlyList<ImportantNodeEligibilityReason> Reasons);
