using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Decisions.ReadDecisionPages;

/// <summary>A bounded, newest-first page of decisions attached to one target.</summary>
public sealed record ArchitectureDecisionPage(
    ArchitectureTarget Target,
    int Offset,
    int Limit,
    int TotalCount,
    IReadOnlyList<ArchitectureDecision> Items);
