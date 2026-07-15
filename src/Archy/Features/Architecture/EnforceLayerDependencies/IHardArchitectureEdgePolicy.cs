using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

public interface IHardArchitectureEdgePolicy
{
    bool IsEligible(GraphEdgeFact edge, ArchitectureEnforcementConfiguration configuration);
}
