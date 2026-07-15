namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>The deterministic architecture condition represented by a baseline finding.</summary>
public enum ArchitectureFindingKind
{
    LayerCoverage,
    LayerDependency,
    DependencyCycle,
}
