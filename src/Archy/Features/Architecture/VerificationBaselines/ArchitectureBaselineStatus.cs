namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Whether the repository baseline can be compared safely with the active rules.</summary>
public enum ArchitectureBaselineStatus
{
    Missing,
    Compatible,
    Incompatible,
}
