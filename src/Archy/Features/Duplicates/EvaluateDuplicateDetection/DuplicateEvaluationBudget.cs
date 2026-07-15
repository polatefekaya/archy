namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

/// <summary>Explicit regression budget for the versioned labelled corpus.</summary>
public sealed record DuplicateEvaluationBudget(double MinimumPrecision, double MinimumRecall, double MaximumFalsePositiveRate)
{
    public static DuplicateEvaluationBudget Default { get; } = new(.80d, .80d, .20d);
}
