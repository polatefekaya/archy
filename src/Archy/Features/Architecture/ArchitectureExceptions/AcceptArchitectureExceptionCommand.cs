using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Explicitly records one reviewed, expiring exception for one currently introduced finding.</summary>
public sealed record AcceptArchitectureExceptionCommand(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride,
    string FindingKey,
    string Author,
    string Reason,
    DateTimeOffset ReviewAtUtc,
    DateTimeOffset ExpiresAtUtc)
    : IRequest<Result<AcceptedArchitectureExceptionDecision>>;
