namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>One policy decision and whether it currently affects enforcement.</summary>
public sealed record ArchitectureExceptionStatus(
    ArchitectureExceptionDecision Exception,
    ArchitectureExceptionState State,
    bool AppliesToIntroducedFinding);
