namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>The complete deterministic comparison between one verification run and its repository baseline.</summary>
public sealed record ArchitectureBaselineComparison(
    ArchitectureBaselineStatus Status,
    string BaselinePath,
    long? BaselineGraphRevision,
    IReadOnlyList<ArchitectureFindingOutcome> Findings)
{
    public IReadOnlyList<ArchitectureFinding> CurrentFindings =>
        [.. Findings
            .Where(static outcome => outcome.Status != ArchitectureFindingStatus.Resolved)
            .Select(static outcome => outcome.Finding)];

    public bool HasIntroducedFindings =>
        Findings.Any(static outcome => outcome.Status == ArchitectureFindingStatus.Introduced);
}
