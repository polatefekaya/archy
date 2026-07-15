namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

/// <summary>Produces deterministic quality metrics and fails a declared precision/recall/false-positive budget.</summary>
public sealed class DuplicateEvaluationReporter : IDuplicateEvaluationReporter
{
    public DuplicateEvaluationReport Evaluate(IReadOnlyDictionary<string, DuplicateEvaluationExpectation> actualByCaseId, DuplicateEvaluationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(actualByCaseId);
        ArgumentNullException.ThrowIfNull(budget);
        if (!ValidBudget(budget) || actualByCaseId.Count != DuplicateEvaluationCorpus.Cases.Count || DuplicateEvaluationCorpus.Cases.Any(item => !actualByCaseId.ContainsKey(item.Id)) || actualByCaseId.Any(item => !Enum.IsDefined(item.Value)))
        {
            throw new ArgumentException("Evaluation requires exactly one supported result for every labelled corpus case and a valid quality budget.", nameof(actualByCaseId));
        }

        var truePositives = 0;
        var falsePositives = 0;
        var falseNegatives = 0;
        var trueNegatives = 0;
        foreach (var item in DuplicateEvaluationCorpus.Cases)
        {
            var expectedDuplicate = item.Expected == DuplicateEvaluationExpectation.LikelyDuplicate;
            var actualDuplicate = actualByCaseId[item.Id] == DuplicateEvaluationExpectation.LikelyDuplicate;
            if (expectedDuplicate && actualDuplicate) truePositives++;
            else if (!expectedDuplicate && actualDuplicate) falsePositives++;
            else if (expectedDuplicate) falseNegatives++;
            else trueNegatives++;
        }

        var precision = truePositives + falsePositives == 0 ? 0d : truePositives / (double)(truePositives + falsePositives);
        var recall = truePositives + falseNegatives == 0 ? 1d : truePositives / (double)(truePositives + falseNegatives);
        var falsePositiveRate = falsePositives + trueNegatives == 0 ? 0d : falsePositives / (double)(falsePositives + trueNegatives);
        precision = Round(precision);
        recall = Round(recall);
        falsePositiveRate = Round(falsePositiveRate);
        return new DuplicateEvaluationReport(DuplicateEvaluationCorpus.Version, truePositives, falsePositives, falseNegatives, trueNegatives, precision, recall, falsePositiveRate,
            precision >= budget.MinimumPrecision && recall >= budget.MinimumRecall && falsePositiveRate <= budget.MaximumFalsePositiveRate);
    }

    private static bool ValidBudget(DuplicateEvaluationBudget budget) => new[] { budget.MinimumPrecision, budget.MinimumRecall, budget.MaximumFalsePositiveRate }.All(static value => double.IsFinite(value) && value is >= 0d and <= 1d);
    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}
