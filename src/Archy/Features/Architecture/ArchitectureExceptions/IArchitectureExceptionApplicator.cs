using Archy.Features.Architecture.VerificationBaselines;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Applies only active exact-match exception decisions to introduced deterministic findings.</summary>
public interface IArchitectureExceptionApplicator
{
    Result<ArchitectureExceptionApplication> Apply(
        ArchitectureBaselineComparison comparison,
        ArchitectureExceptionPolicy policy,
        DateTimeOffset nowUtc);
}
