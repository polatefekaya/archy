namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>One exception-policy lookup, including its canonical path when no policy exists yet.</summary>
public sealed record ArchitectureExceptionPolicyReadResult(
    string Path,
    ArchitectureExceptionPolicy Policy);
