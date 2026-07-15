namespace Archy.Features.Duplicates.CheckDataShapeNaming;

/// <summary>Purpose/name metadata for code intentionally excluded from duplicate-logic comparison.</summary>
public sealed record DataShapeNamingCandidate(string StableId, string DisplayName, DataShapeKind Kind);
