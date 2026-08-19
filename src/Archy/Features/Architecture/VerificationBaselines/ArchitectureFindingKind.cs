namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>The deterministic architecture condition represented by a baseline finding.</summary>
public enum ArchitectureFindingKind
{
    LayerCoverage,
    LayerDependency,
    DependencyCycle,

    /// <summary>
    /// No graph edge in the active revision satisfies the configured hard-edge policy, so no
    /// layer-direction violation and no dependency cycle can be reported whatever the code does.
    /// </summary>
    EnforcementUnavailable,
}
