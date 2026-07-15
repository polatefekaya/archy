namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>A portable accepted-finding set committed with one repository.</summary>
public sealed record ArchitectureVerificationBaseline(
    int SchemaVersion,
    string RuleFingerprint,
    long AcceptedGraphRevision,
    DateTimeOffset AcceptedAtUtc,
    ArchitectureFinding[] Findings);
