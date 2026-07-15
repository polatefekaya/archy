namespace Archy.Features.Duplicates.CheckDataShapeNaming;

/// <summary>A non-blocking naming/convention observation; it is never duplicate-logic evidence.</summary>
public sealed record DataShapeNamingAdvisory(
    string LeftStableId,
    string RightStableId,
    double SemanticSimilarity,
    double NameSimilarity,
    string Message);
