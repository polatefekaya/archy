using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Classifies stable current findings without allowing stale rule baselines to suppress failures.</summary>
public interface IArchitectureBaselineComparer
{
    Result<ArchitectureBaselineComparison> Compare(
        string ruleFingerprint,
        string baselinePath,
        IReadOnlyList<ArchitectureFinding> currentFindings,
        ArchitectureVerificationBaseline? baseline);
}
