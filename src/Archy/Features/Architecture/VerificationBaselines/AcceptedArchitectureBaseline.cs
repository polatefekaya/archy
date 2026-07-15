namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>The portable baseline artifact created by one explicit user acceptance action.</summary>
public sealed record AcceptedArchitectureBaseline(
    string Path,
    long GraphRevision,
    string RuleFingerprint,
    int FindingCount,
    DateTimeOffset AcceptedAtUtc);
