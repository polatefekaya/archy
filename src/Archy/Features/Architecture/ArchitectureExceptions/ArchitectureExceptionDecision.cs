namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>One attributable, time-bounded decision to tolerate one exact deterministic finding.</summary>
public sealed record ArchitectureExceptionDecision(
    string ExceptionId,
    string FindingKey,
    string Author,
    string Reason,
    DateTimeOffset ReviewAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);
