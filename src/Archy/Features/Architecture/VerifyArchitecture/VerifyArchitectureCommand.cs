using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.VerifyArchitecture;

public sealed record VerifyArchitectureCommand(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<ArchitectureVerification>>;
