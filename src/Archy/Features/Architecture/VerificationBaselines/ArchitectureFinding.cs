using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>A stable, evidence-preserving deterministic architecture finding.</summary>
public sealed record ArchitectureFinding(
    string Key,
    ArchitectureFindingKind Kind,
    string Message,
    ArchitectureTarget[] Targets);
