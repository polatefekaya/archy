using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Performs an exact set comparison over rule-compatible stable finding keys.</summary>
public sealed class ArchitectureBaselineComparer : IArchitectureBaselineComparer
{
    public Result<ArchitectureBaselineComparison> Compare(
        string ruleFingerprint,
        string baselinePath,
        IReadOnlyList<ArchitectureFinding> currentFindings,
        ArchitectureVerificationBaseline? baseline)
    {
        if (string.IsNullOrWhiteSpace(ruleFingerprint) ||
            string.IsNullOrWhiteSpace(baselinePath) ||
            currentFindings is null ||
            currentFindings.Any(static finding => finding is null || string.IsNullOrWhiteSpace(finding.Key)) ||
            currentFindings.Select(static finding => finding.Key).Distinct(StringComparer.Ordinal).Count() != currentFindings.Count)
        {
            return ResultFactory.Failure<ArchitectureBaselineComparison>(
                Problem.Validation("Architecture baseline comparison requires unique stable finding keys and a rule fingerprint."));
        }

        if (baseline is null)
        {
            return ResultFactory.Success(new ArchitectureBaselineComparison(
                ArchitectureBaselineStatus.Missing,
                baselinePath,
                null,
                [.. currentFindings
                    .OrderBy(static finding => finding.Key, StringComparer.Ordinal)
                    .Select(static finding => new ArchitectureFindingOutcome(finding, ArchitectureFindingStatus.Introduced))]));
        }

        if (!string.Equals(ruleFingerprint, baseline.RuleFingerprint, StringComparison.Ordinal))
        {
            return ResultFactory.Success(new ArchitectureBaselineComparison(
                ArchitectureBaselineStatus.Incompatible,
                baselinePath,
                baseline.AcceptedGraphRevision,
                [.. currentFindings
                    .OrderBy(static finding => finding.Key, StringComparer.Ordinal)
                    .Select(static finding => new ArchitectureFindingOutcome(finding, ArchitectureFindingStatus.Introduced))]));
        }

        var baselineByKey = baseline.Findings.ToDictionary(static finding => finding.Key, StringComparer.Ordinal);
        var currentByKey = currentFindings.ToDictionary(static finding => finding.Key, StringComparer.Ordinal);
        var outcomes = new List<ArchitectureFindingOutcome>(baselineByKey.Count + currentByKey.Count);
        foreach (var finding in currentFindings.OrderBy(static finding => finding.Key, StringComparer.Ordinal))
        {
            outcomes.Add(new ArchitectureFindingOutcome(
                finding,
                baselineByKey.ContainsKey(finding.Key)
                    ? ArchitectureFindingStatus.Legacy
                    : ArchitectureFindingStatus.Introduced));
        }

        foreach (var finding in baseline.Findings
                     .Where(finding => !currentByKey.ContainsKey(finding.Key))
                     .OrderBy(static finding => finding.Key, StringComparer.Ordinal))
        {
            outcomes.Add(new ArchitectureFindingOutcome(finding, ArchitectureFindingStatus.Resolved));
        }

        return ResultFactory.Success(new ArchitectureBaselineComparison(
            ArchitectureBaselineStatus.Compatible,
            baselinePath,
            baseline.AcceptedGraphRevision,
            outcomes));
    }
}
