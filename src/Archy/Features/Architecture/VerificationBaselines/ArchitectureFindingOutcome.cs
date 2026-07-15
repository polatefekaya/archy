namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>One current or resolved finding together with its baseline classification.</summary>
public sealed record ArchitectureFindingOutcome(
    ArchitectureFinding Finding,
    ArchitectureFindingStatus Status);
