using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Reads and atomically replaces the repository-owned accepted-finding artifact.</summary>
public interface IArchitectureBaselinePolicyRepository
{
    ValueTask<Result<ArchitectureBaselineReadResult>> ReadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken);

    ValueTask<Result<string>> WriteAsync(
        string repositoryRoot,
        ArchitectureVerificationBaseline baseline,
        CancellationToken cancellationToken);
}
