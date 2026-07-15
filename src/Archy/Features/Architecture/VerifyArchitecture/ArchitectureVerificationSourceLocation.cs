namespace Archy.Features.Architecture.VerifyArchitecture;

/// <summary>A repository-relative source location retained for an architecture finding target.</summary>
public sealed record ArchitectureVerificationSourceLocation(
    string NodeStableId,
    string RepositoryRelativePath,
    int? StartLine,
    int? EndLine);
