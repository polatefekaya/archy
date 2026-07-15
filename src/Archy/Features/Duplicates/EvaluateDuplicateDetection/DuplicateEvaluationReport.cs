namespace Archy.Features.Duplicates.EvaluateDuplicateDetection;

public sealed record DuplicateEvaluationReport(
    string CorpusVersion,
    int TruePositives,
    int FalsePositives,
    int FalseNegatives,
    int TrueNegatives,
    double Precision,
    double Recall,
    double FalsePositiveRate,
    bool PassesBudget);
