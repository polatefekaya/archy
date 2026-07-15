using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Reads and appends portable architecture-exception decisions.</summary>
public interface IArchitectureExceptionPolicyRepository
{
    ValueTask<Result<ArchitectureExceptionPolicyReadResult>> ReadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken);

    ValueTask<Result<string>> AppendAsync(
        string repositoryRoot,
        ArchitectureExceptionDecision architectureException,
        CancellationToken cancellationToken);
}
