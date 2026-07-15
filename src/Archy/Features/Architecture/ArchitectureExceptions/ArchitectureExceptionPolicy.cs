namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>The repository-owned append-only exception decision log shared by local verification and CI.</summary>
public sealed record ArchitectureExceptionPolicy(
    int SchemaVersion,
    ArchitectureExceptionDecision[] Exceptions);
