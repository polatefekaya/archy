using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerifyArchitecture;

/// <summary>Runs one complete current-graph verification for command, hook, and MCP callers.</summary>
public interface IArchitectureVerificationService
{
    ValueTask<Result<ArchitectureVerification>> VerifyAsync(
        VerifyArchitectureCommand command,
        CancellationToken cancellationToken);
}
