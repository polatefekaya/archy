using Archy.Features.Duplicates.EvaluateDuplicateDetection;

namespace Archy.UnitTests.Features.Duplicates.EvaluateDuplicateDetection;

public sealed class DuplicateEvaluationReporterTests
{
    [Fact]
    public void EvaluateEmitsVersionedPassingQualityReportForTheCorpusBaseline()
    {
        var actual = DuplicateEvaluationCorpus.Cases.ToDictionary(static item => item.Id, static item => item.Expected, StringComparer.Ordinal);

        var report = new DuplicateEvaluationReporter().Evaluate(actual, DuplicateEvaluationBudget.Default);

        Assert.Equal(DuplicateEvaluationCorpus.Version, report.CorpusVersion);
        Assert.Equal(3, report.TruePositives);
        Assert.Equal(0, report.FalsePositives);
        Assert.Equal(1d, report.Precision);
        Assert.Equal(1d, report.Recall);
        Assert.True(report.PassesBudget);
    }

    [Fact]
    public void EvaluateFailsTheQualityBudgetWhenANegativeIsMisclassifiedAsADuplicate()
    {
        var actual = DuplicateEvaluationCorpus.Cases.ToDictionary(static item => item.Id, static item => item.Expected, StringComparer.Ordinal);
        actual["dto-record"] = DuplicateEvaluationExpectation.LikelyDuplicate;

        var report = new DuplicateEvaluationReporter().Evaluate(actual, DuplicateEvaluationBudget.Default);

        Assert.Equal(1, report.FalsePositives);
        Assert.False(report.PassesBudget);
    }
}
