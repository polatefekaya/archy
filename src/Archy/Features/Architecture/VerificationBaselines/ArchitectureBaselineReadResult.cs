namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>One baseline lookup, including the canonical policy-artifact path when no file exists yet.</summary>
public sealed record ArchitectureBaselineReadResult(
    string Path,
    ArchitectureVerificationBaseline? Baseline);
