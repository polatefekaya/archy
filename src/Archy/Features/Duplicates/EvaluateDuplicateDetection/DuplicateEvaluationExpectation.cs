namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

public enum DuplicateEvaluationExpectation
{
    LikelyDuplicate = 1,
    NotDuplicate = 2,
    DataShapeNamingAdvisory = 3,
    ExcludedGeneratedSource = 4,
    SuppressedByExactDecision = 5,
}
