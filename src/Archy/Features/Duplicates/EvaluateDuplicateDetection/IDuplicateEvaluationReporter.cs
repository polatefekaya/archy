namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

public interface IDuplicateEvaluationReporter
{
    DuplicateEvaluationReport Evaluate(IReadOnlyDictionary<string, DuplicateEvaluationExpectation> actualByCaseId, DuplicateEvaluationBudget budget);
}
