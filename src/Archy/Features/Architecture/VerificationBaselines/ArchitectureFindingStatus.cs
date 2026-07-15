namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>How a deterministic finding relates to the accepted repository baseline.</summary>
public enum ArchitectureFindingStatus
{
    Introduced,
    Legacy,
    Resolved,
    Excepted,
}
