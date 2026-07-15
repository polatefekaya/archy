using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.VerifyArchitecture;

/// <summary>Provides the Mediator command boundary for current-graph architecture verification.</summary>
public sealed class VerifyArchitectureHandler(IArchitectureVerificationService verificationService)
    : IRequestHandler<VerifyArchitectureCommand, Result<ArchitectureVerification>>
{
    public async ValueTask<Result<ArchitectureVerification>> Handle(
        VerifyArchitectureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await verificationService.VerifyAsync(command, cancellationToken);
    }
}
