using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.DetectDependencyCycles;

public interface IArchitectureCycleDetector
{
    Result<IReadOnlyList<ArchitectureDependencyCycle>> Detect(
        IReadOnlyList<LayerMembershipResolution> membership,
        IReadOnlyList<GraphEdgeFact> eligibleEdges);
}
