using Archy.Features.Duplicates.DuplicateFindings;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.AggregateDuplicateSignals;

/// <summary>All independently-produced evidence for one unordered graph-node candidate pair at one revision.</summary>
public sealed record DuplicateSignalSet(
    ArchitectureTarget LeftTarget,
    ArchitectureTarget RightTarget,
    long GraphRevision,
    IReadOnlyList<DuplicateSignalFact> Signals);
