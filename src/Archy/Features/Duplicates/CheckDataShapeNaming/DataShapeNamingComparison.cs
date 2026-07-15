namespace Archy.Features.Duplicates.CheckDataShapeNaming;

public sealed record DataShapeNamingComparison(
    DataShapeNamingCandidate Left,
    DataShapeNamingCandidate Right,
    double SemanticSimilarity);
