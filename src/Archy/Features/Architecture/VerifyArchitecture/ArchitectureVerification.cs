using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.VerificationBaselines;

namespace Archy.Features.Architecture.VerifyArchitecture;

/// <summary>The deterministic architecture-rule result for one current graph revision.</summary>
public sealed record ArchitectureVerification(
    long GraphRevision,
    bool AnalysisWasNoOp,
    string RuleFingerprint,
    LayerDependencyEvaluation Evaluation,
    ArchitectureBaselineComparison Baseline,
    IReadOnlyList<ArchitectureExceptionStatus> Exceptions,
    IReadOnlyList<ArchitectureVerificationSourceLocation> SourceLocations)
{
    /// <summary>Only newly introduced deterministic findings block normal local and CI verification.</summary>
    public bool IsCompliant => !Baseline.HasIntroducedFindings;
}
