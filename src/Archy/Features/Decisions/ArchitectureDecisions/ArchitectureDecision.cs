using Archy.SharedKernel.Primitives;

namespace Archy.Features.Decisions.ArchitectureDecisions;

public sealed record ArchitectureDecision(
    string DecisionId,
    string DecisionType,
    DecisionResolution Resolution,
    string? Note,
    string ActorKind,
    string ActorId,
    string? SessionId,
    long? GraphRevision,
    IReadOnlyList<ArchitectureTarget> Targets,
    DateTimeOffset OccurredAtUtc);
