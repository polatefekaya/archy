namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>The portable exception decision created by an explicit acceptance command.</summary>
public sealed record AcceptedArchitectureExceptionDecision(
    string Path,
    string ExceptionId,
    string FindingKey,
    DateTimeOffset ReviewAtUtc,
    DateTimeOffset ExpiresAtUtc);
