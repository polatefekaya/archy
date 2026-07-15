namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

/// <summary>A reproducible graph-source reference; source text is intentionally not copied into the inspection payload.</summary>
public sealed record DuplicateInspectionSide(string StableId, string DisplayName, string? RepositoryRelativePath, int? StartLine, int? EndLine);
