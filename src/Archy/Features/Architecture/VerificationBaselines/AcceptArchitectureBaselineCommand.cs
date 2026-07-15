using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Explicitly replaces the repository baseline with all findings visible in one verified graph revision.</summary>
public sealed record AcceptArchitectureBaselineCommand(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<AcceptedArchitectureBaseline>>;
