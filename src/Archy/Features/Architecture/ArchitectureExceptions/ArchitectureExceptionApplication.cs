using Archy.Features.Architecture.VerificationBaselines;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>The exception-adjusted verification comparison plus all visible decision states.</summary>
public sealed record ArchitectureExceptionApplication(
    ArchitectureBaselineComparison Comparison,
    IReadOnlyList<ArchitectureExceptionStatus> Exceptions);
